"""Export the TAEHV encoder/decoder (taew2_1.pth) to ONNX with dynamic time and spatial axes.

usage: python export-vae.py --taehv <taew2_1.pth> --out <dir> [--verify]
"""
import argparse
import os

import numpy as np
import torch

from taehv import Decoder, Encoder, load_taehv


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--taehv", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    model = load_taehv(args.taehv)
    encoder = Encoder(model).eval()
    decoder = Decoder(model).eval()

    frames = torch.rand(1, 12, 3, 256, 256)
    latents = torch.randn(1, 3, 16, 32, 32)

    t = torch.export.Dim("frames", min=4, max=64)
    s_h = torch.export.Dim("height", min=8, max=2048)
    s_w = torch.export.Dim("width", min=8, max=2048)
    with torch.no_grad():
        encoded = encoder(frames)
        decoded = decoder(latents)
        print("encoder", tuple(frames.shape), "->", tuple(encoded.shape))
        print("decoder", tuple(latents.shape), "->", tuple(decoded.shape))
        enc_path = os.path.join(args.out, "vae_encoder.onnx")
        dec_path = os.path.join(args.out, "vae_decoder.onnx")
        torch.onnx.export(encoder, (frames,), dynamo=True, input_names=["frames"], output_names=["latents"],
                          dynamic_shapes={"frames": {1: 4 * torch.export.Dim("latent_frames", min=1, max=16),
                                                     3: 8 * s_h, 4: 8 * s_w}},
                          opset_version=18).save(enc_path)
        torch.onnx.export(decoder, (latents,), dynamo=True, input_names=["latents"], output_names=["frames"],
                          dynamic_shapes={"latents": {1: torch.export.Dim("latent_frames_d", min=1, max=16),
                                                      3: s_h, 4: s_w}},
                          opset_version=18).save(dec_path)

    if args.verify:
        import onnxruntime as ort
        enc = ort.InferenceSession(enc_path, providers=["CPUExecutionProvider"])
        dec = ort.InferenceSession(dec_path, providers=["CPUExecutionProvider"])
        for shape in [(1, 12, 3, 256, 256), (1, 4, 3, 128, 192)]:
            x = torch.rand(*shape)
            with torch.no_grad():
                expected = encoder(x).numpy()
            actual = enc.run(None, {"frames": x.numpy()})[0]
            print("encoder", shape, "max abs diff", float(np.abs(expected - actual).max()))
        for shape in [(1, 3, 16, 32, 32), (1, 1, 16, 16, 24)]:
            z = torch.randn(*shape)
            with torch.no_grad():
                expected = decoder(z).numpy()
            actual = dec.run(None, {"latents": z.numpy()})[0]
            print("decoder", shape, "->", actual.shape, "max abs diff", float(np.abs(expected - actual).max()))


if __name__ == "__main__":
    main()
