"""Tiny Wan2.1 video autoencoder (TAEHV, taew2_1.pth) in parallel-over-time form.

Pixels are RGB in [0, 1]; latents are the raw (unnormalized) Wan latents that the V2 DiT works in.
"""
import torch
import torch.nn as nn
import torch.nn.functional as F

LATENT_CHANNELS = 16
IMAGE_CHANNELS = 3


def conv(n_in, n_out, **kwargs):
    return nn.Conv2d(n_in, n_out, 3, padding=1, **kwargs)


class Clamp(nn.Module):
    def forward(self, x):
        return torch.tanh(x / 3) * 3


class MemBlock(nn.Module):
    def __init__(self, n_in, n_out):
        super().__init__()
        self.conv = nn.Sequential(conv(n_in * 2, n_out), nn.ReLU(), conv(n_out, n_out), nn.ReLU(), conv(n_out, n_out))
        self.skip = nn.Conv2d(n_in, n_out, 1, bias=False) if n_in != n_out else nn.Identity()
        self.act = nn.ReLU()

    def forward(self, x, past):
        return self.act(self.conv(torch.cat([x, past], 1)) + self.skip(x))


class TPool(nn.Module):
    def __init__(self, n_f, stride):
        super().__init__()
        self.stride = stride
        self.conv = nn.Conv2d(n_f * stride, n_f, 1, bias=False)

    def forward(self, x):
        _, c, h, w = x.shape
        return self.conv(x.reshape(-1, self.stride * c, h, w))


class TGrow(nn.Module):
    def __init__(self, n_f, stride):
        super().__init__()
        self.stride = stride
        self.conv = nn.Conv2d(n_f, n_f * stride, 1, bias=False)

    def forward(self, x):
        _, c, h, w = x.shape
        return self.conv(x).reshape(-1, c, h, w)


def run_over_time(model, x):
    """x: [1, T, C, H, W]; every MemBlock sees the previous time step's input (zeros at t=0)."""
    n, t, c, h, w = x.shape
    x = x.reshape(n * t, c, h, w)
    for block in model:
        if isinstance(block, MemBlock):
            nt, c, h, w = x.shape
            t = nt // n
            frames = x.reshape(n, t, c, h, w)
            past = F.pad(frames, (0, 0, 0, 0, 0, 0, 1, 0))[:, :t].reshape(x.shape)
            x = block(x, past)
        else:
            x = block(x)
    nt, c, h, w = x.shape
    return x.view(n, nt // n, c, h, w)


class Taehv(nn.Module):
    def __init__(self):
        super().__init__()
        self.encoder = nn.Sequential(
            conv(IMAGE_CHANNELS, 64), nn.ReLU(), TPool(64, 2), conv(64, 64, stride=2, bias=False),
            MemBlock(64, 64), MemBlock(64, 64), MemBlock(64, 64), TPool(64, 2), conv(64, 64, stride=2, bias=False),
            MemBlock(64, 64), MemBlock(64, 64), MemBlock(64, 64), TPool(64, 1), conv(64, 64, stride=2, bias=False),
            MemBlock(64, 64), MemBlock(64, 64), MemBlock(64, 64), conv(64, LATENT_CHANNELS))
        f = [256, 128, 64, 64]
        self.decoder = nn.Sequential(
            Clamp(), conv(LATENT_CHANNELS, f[0]), nn.ReLU(),
            MemBlock(f[0], f[0]), MemBlock(f[0], f[0]), MemBlock(f[0], f[0]), nn.Upsample(scale_factor=2), TGrow(f[0], 1),
            conv(f[0], f[1], bias=False),
            MemBlock(f[1], f[1]), MemBlock(f[1], f[1]), MemBlock(f[1], f[1]), nn.Upsample(scale_factor=2), TGrow(f[1], 2),
            conv(f[1], f[2], bias=False),
            MemBlock(f[2], f[2]), MemBlock(f[2], f[2]), MemBlock(f[2], f[2]), nn.Upsample(scale_factor=2), TGrow(f[2], 2),
            conv(f[2], f[3], bias=False), nn.ReLU(), conv(f[3], IMAGE_CHANNELS))


class Encoder(nn.Module):
    """[1, 4*T, 3, H, W] pixels in [0,1] -> [1, T, 16, H/8, W/8] latents."""

    def __init__(self, taehv):
        super().__init__()
        self.net = taehv.encoder

    def forward(self, frames):
        return run_over_time(self.net, frames)


class Decoder(nn.Module):
    """[1, T, 16, h, w] latents -> [1, 4*T, 3, 8h, 8w] pixels in [0,1] (unclamped)."""

    def __init__(self, taehv):
        super().__init__()
        self.net = taehv.decoder

    def forward(self, latents):
        return run_over_time(self.net, latents)


def load_taehv(path):
    model = Taehv()
    state = torch.load(path, map_location="cpu", weights_only=True)
    own = model.state_dict()
    for index, layer in enumerate(model.decoder):
        key = f"decoder.{index}.conv.weight"
        if isinstance(layer, TGrow) and state[key].shape[0] > own[key].shape[0]:
            state[key] = state[key][-own[key].shape[0]:]
    model.load_state_dict(state)
    return model.eval().requires_grad_(False)
