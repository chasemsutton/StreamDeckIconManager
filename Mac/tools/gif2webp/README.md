# gif2webp (macOS)

Google's GIF to WebP tools for the Mac build, from the official libwebp release. Same version and
purpose as the Windows copies in `Add Stream Deck Icon/tools/gif2webp`: `gif2webp` turns GIF icons
into lossless animated WebP, and `img2webp` rebuilds animated icons at Stream Deck's 144 px size.

- Version: libwebp 1.5.0, `bin/gif2webp` and `bin/img2webp` from
  - `libwebp-1.5.0-mac-arm64.tar.gz` (SHA-256 `0fa3d7fb64a2b2849115c2f1052b7068b3fbadcb7263e3f1840fcd8c798ea9c2`) → `osx-arm64/`
  - `libwebp-1.5.0-mac-x86-64.tar.gz` (SHA-256 `f8917c9d193dd13ae2266272e1b491436a846fc328922affad572ec0e8d73d87`) → `osx-x64/`
- Downloaded from: https://storage.googleapis.com/downloads.webmproject.org/releases/webp/
- Signature: both archives checked against their `.asc` files, good signature from
  "WebP release signing key <webp-discuss@webmproject.org>", fingerprint
  `6B0E 6B70 976D E303 EDF2 F601 F9C3 D6BD B823 2B5D`
- File SHA-256:
  - `osx-arm64/gif2webp` `6361d921ae40db1c030704a3894725b36149248805748ee463d118d46c024777`
  - `osx-arm64/img2webp` `760399b84bf268c17f37641004b209b7e98278a09dfa2b79d05bf3045d9a6bf7`
  - `osx-x64/gif2webp` `f3dc652472ca22ed8aa5fcc3660806e26c2ca8bec9c1a53b0bed85db893dcf83`
  - `osx-x64/img2webp` `3d7e2e3150595d22300a8646acd44f55f0bd6eff08a534e33e17565eb49d52ea`
- Dependencies: only macOS system libraries (`libSystem`, and `libc++` for img2webp).
- License: BSD 3-clause (`COPYING.txt`) with the WebM patent grant (`PATENTS.txt`).
