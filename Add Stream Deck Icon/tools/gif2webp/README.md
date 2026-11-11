# gif2webp

Google's GIF to WebP converter, from the official libwebp release. The app uses it to turn GIF
icons into lossless animated WebP, which Stream Deck's icon library previews (it shows GIFs as
broken images in a pack's icon grid).

- Version: libwebp 1.5.0, `libwebp-1.5.0-windows-x64.zip`, `bin/gif2webp.exe`
- Downloaded from: https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.5.0-windows-x64.zip
- Release archive SHA-256: `e8fe3bc7eb09774e69261a42bf9fa8a37ab5f3eecaab199f6420e6f9e822090c`
- Signature: checked against `libwebp-1.5.0-windows-x64.zip.asc`, good signature from
  "WebP release signing key <webp-discuss@webmproject.org>", fingerprint
  `6B0E 6B70 976D E303 EDF2 F601 F9C3 D6BD B823 2B5D`
- `gif2webp.exe` SHA-256: `C00667DC15C509EF979A153F726B9F165FB16E521EDC4986D9BC7ACC12725CA3`
- `img2webp.exe` (same archive, `bin/img2webp.exe`) SHA-256: `3096525017A1F79EC9703E082D73B93E44394BCC40140FA68AB23A341301D95A`.
  The app uses it to rebuild animated icons at Stream Deck's 144 px size, frame by frame with
  each frame's original duration.
- License: BSD 3-clause (`COPYING.txt`) with the WebM patent grant (`PATENTS.txt`), taken from
  the libwebp source at tag `v1.5.0`.

Conversion is lossless by default: every frame is pixel-identical to the GIF and frame timing is
kept. The tool runs on its own and needs no other files.
