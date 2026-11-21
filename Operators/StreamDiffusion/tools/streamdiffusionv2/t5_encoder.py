"""UMT5-XXL encoder with the Wan2.1 checkpoint layout (per-layer relative position bias)."""
import math

import torch
import torch.nn as nn
import torch.nn.functional as F

VOCAB = 256384
DIM = 4096
FFN_DIM = 10240
NUM_HEADS = 64
NUM_LAYERS = 24
NUM_BUCKETS = 32
MAX_DIST = 128
SEQ_LEN = 512
EPS = 1e-6


class CastLinear(nn.Linear):
    """Stores bf16 weights (half the file size) but computes in fp32; fp16 overflows in this model."""

    def forward(self, x):
        return F.linear(x, self.weight.float())


class LayerNorm(nn.Module):
    def __init__(self):
        super().__init__()
        self.weight = nn.Parameter(torch.ones(DIM))

    def forward(self, x):
        x = x * torch.rsqrt(x.pow(2).mean(dim=-1, keepdim=True) + EPS)
        return self.weight.float() * x


class Attention(nn.Module):
    def __init__(self):
        super().__init__()
        self.q = CastLinear(DIM, DIM, bias=False)
        self.k = CastLinear(DIM, DIM, bias=False)
        self.v = CastLinear(DIM, DIM, bias=False)
        self.o = CastLinear(DIM, DIM, bias=False)


class Gate(nn.Sequential):
    def __init__(self):
        super().__init__(CastLinear(DIM, FFN_DIM, bias=False), nn.GELU(approximate="tanh"))


class FeedForward(nn.Module):
    def __init__(self):
        super().__init__()
        self.gate = Gate()
        self.fc1 = CastLinear(DIM, FFN_DIM, bias=False)
        self.fc2 = CastLinear(FFN_DIM, DIM, bias=False)

    def forward(self, x):
        return self.fc2(self.fc1(x) * self.gate(x))


class RelativeEmbedding(nn.Module):
    def __init__(self):
        super().__init__()
        self.embedding = nn.Embedding(NUM_BUCKETS, NUM_HEADS)


def _bucket_indices():
    """Bidirectional T5 relative-position buckets for a fixed SEQ_LEN, [SEQ_LEN, SEQ_LEN]."""
    rel = torch.arange(SEQ_LEN).unsqueeze(0) - torch.arange(SEQ_LEN).unsqueeze(1)
    half = NUM_BUCKETS // 2
    buckets = (rel > 0).long() * half
    rel = rel.abs()
    max_exact = half // 2
    large = max_exact + (torch.log(rel.float().clamp(min=1) / max_exact) / math.log(MAX_DIST / max_exact)
                         * (half - max_exact)).long()
    large = torch.min(large, torch.full_like(large, half - 1))
    return buckets + torch.where(rel < max_exact, rel, large)


class Block(nn.Module):
    def __init__(self):
        super().__init__()
        self.norm1 = LayerNorm()
        self.attn = Attention()
        self.norm2 = LayerNorm()
        self.ffn = FeedForward()
        self.pos_embedding = RelativeEmbedding()


class T5Encoder(nn.Module):
    def __init__(self):
        super().__init__()
        self.token_embedding = nn.Embedding(VOCAB, DIM)
        self.blocks = nn.ModuleList([Block() for _ in range(NUM_LAYERS)])
        self.norm = LayerNorm()
        self.register_buffer("buckets", _bucket_indices(), persistent=False)

    def forward(self, ids, mask):
        batch = ids.shape[0]
        length = ids.shape[1]
        x = F.embedding(ids, self.token_embedding.weight).float()
        head_dim = DIM // NUM_HEADS
        padding = torch.zeros(batch, 1, 1, length, dtype=torch.float32)
        padding = padding.masked_fill(mask.view(batch, 1, 1, length) == 0, torch.finfo(torch.float32).min)
        buckets = self.buckets[:length, :length]
        for block in self.blocks:
            bias = F.embedding(buckets, block.pos_embedding.embedding.weight).float().permute(2, 0, 1).unsqueeze(0) + padding
            y = block.norm1(x)
            q = block.attn.q(y).view(batch, length, NUM_HEADS, head_dim).transpose(1, 2)
            k = block.attn.k(y).view(batch, length, NUM_HEADS, head_dim).transpose(1, 2)
            v = block.attn.v(y).view(batch, length, NUM_HEADS, head_dim).transpose(1, 2)
            scores = torch.matmul(q, k.transpose(-1, -2)) + bias  # T5 does not scale by 1/sqrt(d)
            attn = torch.softmax(scores.float(), dim=-1).type_as(scores)
            x = x + block.attn.o(torch.matmul(attn, v).transpose(1, 2).reshape(batch, length, DIM))
            x = x + block.ffn(block.norm2(x))
        return self.norm(x).float()
