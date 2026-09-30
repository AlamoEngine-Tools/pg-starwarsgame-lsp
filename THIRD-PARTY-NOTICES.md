# Third-party notices

This project (MIT, see [LICENSE](LICENSE)) redistributes and builds upon third-party components.
Their licenses are reproduced or referenced below.

A complete, machine-readable inventory ships with every release as an SPDX SBOM — one per artifact,
generated in CI on every pull request and attached to each GitHub release:

- `PG.StarWarsGame.LSP.Server-<version>-win-x64.sbom.spdx.json` — the language server
- `aet-eaw-edit-<version>.sbom.spdx.json` — the VS Code extension

Those files are the authoritative dependency list. This document covers the components whose license
terms ask for specific acknowledgement, or where the terms are conditional on how we use them.

---

## Game data

No Petroglyph or LucasArts assets are redistributed by this project. The editor reads game data from
the user's own installation at runtime.

---

## Prior work

### `eaw-lua-debugger` - the Lua debug server protocol

The game's Lua debug server speaks Petroglyph's PGNet protocol, which is not publicly documented by
Petroglyph. The description this project's Lua debugger was built against is
[`starwars_lua_debug_server_protocol.md`](https://github.com/andrewfullard/eaw-lua-debugger) from
**`eaw-lua-debugger`** by **EvilBobTheBob** (GitHub [@andrewfullard](https://github.com/andrewfullard)),
of Phoenix Rising and EaWX - a Python debug client covering the PGNet bitstream, the CRC-checked
UDP framing, the handshake, the reliable ACK/NACK layer and the debugger message encoding.

`eaw-lua-debugger` is MIT licensed, Copyright (c) 2026 Andrew. None of its code is redistributed
here: our adapter is an independent implementation in C#, and the debt is to the protocol
description and its test vectors. Without that work the Lua debugger in this extension would not
exist in its current form.
