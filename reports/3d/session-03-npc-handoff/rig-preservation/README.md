# NPC rig and geometry preservation

Compared the working assets byte-for-byte with `f9d6a4a`.
Materials are intentionally excluded from structural comparison.

| Asset | Structure | BIN payload | Skins | Animations | BIN SHA-256 |
| --- | --- | --- | ---: | ---: | --- |
| `assets/models/characters/npc_adventurer_f.glb` | PASS | PASS | 4 | 24 | `34ec2faafeea89fca5f85c7f4054f9056f5cf95760c7518a2c84d40f78313bdd` |
| `assets/models/characters/npc_guild_rep.glb` | PASS | PASS | 1 | 24 | `5e318a6bd176aae57e3ee00f8667a0e625afe0b2c15ff726d91f4671d26b0326` |
| `assets/models/characters/npc_hooded.glb` | PASS | PASS | 4 | 24 | `ae7f1824f4385cbdd9aedb83af762f8eed165a2eb796f49c3823184e580ada75` |
| `assets/models/characters/npc_innkeeper.glb` | PASS | PASS | 1 | 24 | `bbe2c046a660c63a02ec726693614aa60a2142b550cae229185c015b492b3411` |
| `assets/models/characters/npc_kael.glb` | PASS | PASS | 2 | 24 | `be184f05be4860b6420e3af56368e242ba1bf1dc52f558096d7092ee60146436` |
| `assets/models/characters/npc_merchant_f.glb` | PASS | PASS | 4 | 24 | `37928f561d25ce5e7387d9073c3c38817c4765e2efd5dea76d3f941d34fb0cae` |
| `assets/models/characters/npc_merchant_m.glb` | PASS | PASS | 5 | 24 | `e3315f1063bb39cbf648c8dbf01130eaae71a73970e3a137a5ff6c424431e452` |
| `assets/models/characters/npc_townsman.glb` | PASS | PASS | 4 | 24 | `cdd738234ab6da3a7cd355afeef8a08f9482cbdf33d8d3d86f47019c51602ee4` |
| `assets/models/characters/npc_townswoman.glb` | PASS | PASS | 4 | 24 | `ac8b984e70bcf8fc7d695f3e9c17e3591ff0f5d87396c1d25ad7fd0f7b7b3b50` |
| `assets/models/characters/npc_vendor.glb` | PASS | PASS | 1 | 24 | `9fd5c2769ed2322eb463deed5671404b27a77ac33d2b4e44f4241dc4b6f85bb3` |
| `assets/models/characters/npc_woman_dress.glb` | PASS | PASS | 1 | 11 | `cb9a79f63e0d8412ed383ceef38524c848ec699ff40ad151ca1fee1e56a441f4` |

Result: **PASS** (11 production humans).
