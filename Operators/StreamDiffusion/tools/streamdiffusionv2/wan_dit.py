"""Stateless single-frame step of the causal Wan2.1 1.3B DiT used by StreamDiffusionV2.

The reference model mutates per-layer KV caches in place with data-dependent control flow, which
does not export to ONNX. This module keeps all cache bookkeeping outside the graph: the host owns
the slot policy and passes in (a) the cache tensors, (b) an additive validity mask over cache
slots, (c) the slot the new frame is written to. Attention treats the cache as an unordered set
(RoPE is baked into cached keys), so slot order does not matter.
"""
import math

import torch
import torch.nn as nn
import torch.nn.functional as F

DIM = 1536
FFN_DIM = 8960
NUM_HEADS = 12
HEAD_DIM = 128
NUM_LAYERS = 30
FREQ_DIM = 256
TEXT_DIM = 4096
TEXT_LEN = 512
LATENT_CHANNELS = 16
PATCH = 2
EPS = 1e-6
MAX_ROPE_FRAMES = 1024


class RmsNorm(nn.Module):
    def __init__(self, dim):
        super().__init__()
        self.weight = nn.Parameter(torch.ones(dim))

    def forward(self, x):
        xf = x.float()
        return (xf * torch.rsqrt(xf.pow(2).mean(-1, keepdim=True) + EPS)).type_as(x) * self.weight


class SelfAttention(nn.Module):
    def __init__(self):
        super().__init__()
        self.q = nn.Linear(DIM, DIM)
        self.k = nn.Linear(DIM, DIM)
        self.v = nn.Linear(DIM, DIM)
        self.o = nn.Linear(DIM, DIM)
        self.norm_q = RmsNorm(DIM)
        self.norm_k = RmsNorm(DIM)


class CrossAttention(nn.Module):
    def __init__(self):
        super().__init__()
        self.q = nn.Linear(DIM, DIM)
        self.k = nn.Linear(DIM, DIM)
        self.v = nn.Linear(DIM, DIM)
        self.o = nn.Linear(DIM, DIM)
        self.norm_q = RmsNorm(DIM)
        self.norm_k = RmsNorm(DIM)


class Block(nn.Module):
    def __init__(self):
        super().__init__()
        self.norm1 = nn.LayerNorm(DIM, eps=EPS, elementwise_affine=False)
        self.self_attn = SelfAttention()
        self.norm3 = nn.LayerNorm(DIM, eps=EPS, elementwise_affine=True)
        self.cross_attn = CrossAttention()
        self.norm2 = nn.LayerNorm(DIM, eps=EPS, elementwise_affine=False)
        self.ffn = nn.Sequential(nn.Linear(DIM, FFN_DIM), nn.GELU(approximate="tanh"), nn.Linear(FFN_DIM, DIM))
        self.modulation = nn.Parameter(torch.zeros(1, 6, DIM))


class Head(nn.Module):
    def __init__(self):
        super().__init__()
        self.norm = nn.LayerNorm(DIM, eps=EPS, elementwise_affine=False)
        self.head = nn.Linear(DIM, PATCH * PATCH * LATENT_CHANNELS)
        self.modulation = nn.Parameter(torch.zeros(1, 2, DIM))


def _rope_angles():
    """Per-axis rotation angles: temporal 22 pairs, height 21, width 21 (head_dim 128)."""
    dims = [HEAD_DIM - 4 * (HEAD_DIM // 6), 2 * (HEAD_DIM // 6), 2 * (HEAD_DIM // 6)]
    tables = []
    for dim in dims:
        inv = 1.0 / torch.pow(10000.0, torch.arange(0, dim, 2).double() / dim)
        tables.append(torch.outer(torch.arange(MAX_ROPE_FRAMES).double(), inv))
    return tables


class WanCausalCore(nn.Module):
    """Parameter container with the same state-dict layout as the reference `model.*` weights."""

    def __init__(self):
        super().__init__()
        self.patch_embedding = nn.Conv3d(LATENT_CHANNELS, DIM, kernel_size=(1, PATCH, PATCH), stride=(1, PATCH, PATCH))
        self.text_embedding = nn.Sequential(nn.Linear(TEXT_DIM, DIM), nn.GELU(approximate="tanh"), nn.Linear(DIM, DIM))
        self.time_embedding = nn.Sequential(nn.Linear(FREQ_DIM, DIM), nn.SiLU(), nn.Linear(DIM, DIM))
        self.time_projection = nn.Sequential(nn.SiLU(), nn.Linear(DIM, DIM * 6))
        self.blocks = nn.ModuleList([Block() for _ in range(NUM_LAYERS)])
        self.head = Head()


def load_core(checkpoint_path):
    core = WanCausalCore()
    state = torch.load(checkpoint_path, map_location="cpu", weights_only=True, mmap=True)
    state = state["generator"]
    state = {k[len("model."):]: v for k, v in state.items() if k.startswith("model.")}
    missing, unexpected = core.load_state_dict(state, strict=False)
    if missing or unexpected:
        raise RuntimeError(f"state dict mismatch: missing={missing[:5]} unexpected={unexpected[:5]}")
    return core.eval().requires_grad_(False)


class CrossKvModel(nn.Module):
    """Prompt embeddings (T5 output, zero padded to 512) -> per-layer cross-attention K/V."""

    def __init__(self, core, dtype=torch.float32):
        super().__init__()
        self.core = core
        self.dtype = dtype

    def forward(self, prompt_embeds):
        context = self.core.text_embedding(prompt_embeds.to(self.dtype))
        keys = []
        values = []
        for block in self.core.blocks:
            attn = block.cross_attn
            keys.append(attn.norm_k(attn.k(context)).view(1, TEXT_LEN, NUM_HEADS, HEAD_DIM))
            values.append(attn.v(context).view(1, TEXT_LEN, NUM_HEADS, HEAD_DIM))
        return torch.stack(keys, 0).half(), torch.stack(values, 0).half()


class StepModel(nn.Module):
    """One denoising step for a batch of single latent frames, each with its own KV cache."""

    def __init__(self, core, latent_height, latent_width, dtype=torch.float32):
        super().__init__()
        self.core = core
        self.dtype = dtype
        self.grid_h = latent_height // PATCH
        self.grid_w = latent_width // PATCH
        self.tokens = self.grid_h * self.grid_w
        angles = _rope_angles()
        self.register_buffer("cos_t", angles[0].cos().float(), persistent=False)
        self.register_buffer("sin_t", angles[0].sin().float(), persistent=False)
        self.register_buffer("cos_h", angles[1][: self.grid_h].cos().float(), persistent=False)
        self.register_buffer("sin_h", angles[1][: self.grid_h].sin().float(), persistent=False)
        self.register_buffer("cos_w", angles[2][: self.grid_w].cos().float(), persistent=False)
        self.register_buffer("sin_w", angles[2][: self.grid_w].sin().float(), persistent=False)
        half = FREQ_DIM // 2
        inv = torch.pow(10000.0, -torch.arange(half).double() / half).float()
        self.register_buffer("time_inv_freq", inv, persistent=False)

    def _rope_tables(self, frame_pos):
        batch = frame_pos.shape[0]
        gh, gw = self.grid_h, self.grid_w
        ct = self.cos_t[frame_pos].unsqueeze(1).expand(batch, self.tokens, -1)
        st = self.sin_t[frame_pos].unsqueeze(1).expand(batch, self.tokens, -1)
        ch = self.cos_h.repeat_interleave(gw, dim=0).unsqueeze(0).expand(batch, -1, -1)
        sh = self.sin_h.repeat_interleave(gw, dim=0).unsqueeze(0).expand(batch, -1, -1)
        cw = self.cos_w.repeat(gh, 1).unsqueeze(0).expand(batch, -1, -1)
        sw = self.sin_w.repeat(gh, 1).unsqueeze(0).expand(batch, -1, -1)
        cos = torch.cat([ct, ch, cw], dim=-1).unsqueeze(2)
        sin = torch.cat([st, sh, sw], dim=-1).unsqueeze(2)
        return cos, sin

    @staticmethod
    def _apply_rope(x, cos, sin):
        pairs = x.float().view(x.shape[0], x.shape[1], NUM_HEADS, HEAD_DIM // 2, 2)
        even = pairs[..., 0]
        odd = pairs[..., 1]
        return torch.stack([even * cos - odd * sin, even * sin + odd * cos], dim=-1).flatten(3).to(x.dtype)

    def forward(self, latent, timestep, sigma, frame_pos, k_cache, v_cache, cache_mask, slot, cross_k, cross_v):
        batch = latent.shape[0]
        core = self.core
        cos, sin = self._rope_tables(frame_pos)

        patches = core.patch_embedding(latent.to(self.dtype).unsqueeze(2))
        x = patches.flatten(2).transpose(1, 2)

        angle = timestep.unsqueeze(1) * self.time_inv_freq.unsqueeze(0)
        time_features = torch.cat([angle.cos(), angle.sin()], dim=1).to(self.dtype)
        e = core.time_embedding(time_features)
        e0 = core.time_projection(e).view(batch, 6, DIM)

        # Cache slots first, the new frame's own tokens last; new tokens are always visible.
        attn_mask = torch.cat([cache_mask, torch.zeros(batch, self.tokens, dtype=cache_mask.dtype)], dim=1).to(self.dtype)
        attn_mask = attn_mask.view(batch, 1, 1, -1).expand(batch, 1, self.tokens, -1)

        new_keys = []
        new_values = []
        for index, block in enumerate(core.blocks):
            mod = (block.modulation + e0).chunk(6, dim=1)
            sa = block.self_attn
            y = block.norm1(x) * (1 + mod[1]) + mod[0]
            q = self._apply_rope(sa.norm_q(sa.q(y)).view(batch, self.tokens, NUM_HEADS, HEAD_DIM), cos, sin)
            k = self._apply_rope(sa.norm_k(sa.k(y)).view(batch, self.tokens, NUM_HEADS, HEAD_DIM), cos, sin)
            v = sa.v(y).view(batch, self.tokens, NUM_HEADS, HEAD_DIM)
            new_keys.append(k)
            new_values.append(v)

            all_k = torch.cat([k_cache[index].to(self.dtype), k], dim=1)
            all_v = torch.cat([v_cache[index].to(self.dtype), v], dim=1)
            attn = F.scaled_dot_product_attention(
                q.transpose(1, 2), all_k.transpose(1, 2), all_v.transpose(1, 2), attn_mask=attn_mask)
            attn = attn.transpose(1, 2).flatten(2)
            x = x + sa.o(attn) * mod[2]

            ca = block.cross_attn
            cq = ca.norm_q(ca.q(block.norm3(x))).view(batch, self.tokens, NUM_HEADS, HEAD_DIM)
            cattn = F.scaled_dot_product_attention(
                cq.transpose(1, 2),
                cross_k[index].to(self.dtype).transpose(1, 2).expand(batch, -1, -1, -1),
                cross_v[index].to(self.dtype).transpose(1, 2).expand(batch, -1, -1, -1))
            x = x + ca.o(cattn.transpose(1, 2).flatten(2))

            y = block.ffn(block.norm2(x) * (1 + mod[4]) + mod[3])
            x = x + y * mod[5]

        head_mod = (core.head.modulation + e.view(batch, 1, DIM)).chunk(2, dim=1)
        out = core.head.head(core.head.norm(x) * (1 + head_mod[1]) + head_mod[0])

        gh, gw = self.grid_h, self.grid_w
        flow = out.view(batch, gh, gw, PATCH, PATCH, LATENT_CHANNELS).permute(0, 5, 1, 3, 2, 4)
        flow = flow.reshape(batch, LATENT_CHANNELS, gh * PATCH, gw * PATCH).float()
        x0 = latent - sigma.view(batch, 1, 1, 1) * flow

        new_k = torch.stack(new_keys, 0).half()
        new_v = torch.stack(new_values, 0).half()
        positions = slot.unsqueeze(1) * self.tokens + torch.arange(self.tokens).unsqueeze(0)
        batch_index = torch.arange(batch).unsqueeze(1).expand(batch, self.tokens)
        k_out = k_cache.clone()
        v_out = v_cache.clone()
        k_out[:, batch_index, positions] = new_k
        v_out[:, batch_index, positions] = new_v
        return x0, k_out, v_out
