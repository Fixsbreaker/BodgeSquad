<div align="center">

<img src="Docs/KeyArt/keyart_house.webp" alt="Bodge Squad key art" width="720">

# Bodge Squad

**Bodge it · Patch it · Hide it · Pass!?**

An online co-op comedy game about doing the bare minimum and getting away with it.

[![Play on itch.io](https://img.shields.io/badge/itch.io-Bodge%20Squad-FA5C5C?style=for-the-badge&logo=itch.io&logoColor=white)](https://aybekk.itch.io/bodge-squad)
![Unity](https://img.shields.io/badge/Unity-6000.6-000000?style=for-the-badge&logo=unity&logoColor=white)
![Players](https://img.shields.io/badge/players-1--4%20online-F6C93B?style=for-the-badge)
![Status](https://img.shields.io/badge/status-in%20development-4E7F79?style=for-the-badge)

### 👉 [aybekk.itch.io/bodge-squad](https://aybekk.itch.io/bodge-squad)

</div>

---

## The pitch

You and up to three friends get a wrecked house and a fixed budget. Buy the cheapest materials you can find, patch what you can, and hide what you can't afford to fix. Then the inspector shows up, and you only get paid for what they don't notice.

Up to four people, each on their own PC, who absolutely should not be trusted with power tools. One hosts, the others join with a room code.

<p align="center"><img src="Docs/menu.png" alt="Title screen" width="80%"></p>

<p align="center">
  <img src="Docs/keyart_lineup.png" alt="The four bodgers and the inspector" width="49%">
  <img src="Docs/keyart_overview.png" alt="The house" width="49%">
</p>

## How a job plays

1. **Read the work order.** Every defect in the house is listed with its severity: cosmetic, *will fail later*, or **safety hazard**.
2. **Shop on a shared budget.** Grab supplies off the table and you've paid for them. Blue tags are proper fixes (safe, expensive). Red tags are bodges like paint, wallpaper and duct tape (cheap, risky).
3. **Fix it, hide it, or shove a wardrobe in front of it.** Furniture and pictures hide damage for free: pick one up, look at a defect, and it snaps into place.
4. **Ring the doorbell.** The inspector walks the house, taps the walls and peers behind the sofa. Each bodge they catch gets a red flag and costs you money.
5. **Get the report.** Every defect, who bodged it, the payout and your profit.

Fixing everything properly costs more than the budget, so the game is about deciding what to cut.

<p align="center">
  <img src="Docs/keyart_cover.png" alt="A picture hung over a crack, a bookshelf hiding a hole" width="49%">
  <img src="Docs/keyart_inspector.png" alt="The inspector" width="49%">
</p>

## Design notes

- **One rule table.** The chance of getting caught depends only on *how* you hid something and *how bad* it was ([`RepairRules.cs`](Assets/Scripts/Job/RepairRules.cs)). The same shortcut on the same severity always carries the same risk, so players can learn it.
- **Real inspection categories.** Severity levels follow the structure of real home-inspection reports: cosmetic, future failure, safety.
- **A physical shop.** Picking an item up pays for it, so the budget is something you can see on the table, not a menu.
- **A live inspector.** Flags go up one at a time in front of everyone, so the payoff happens in the room, not on a results screen.
- **A risk meter by the crosshair.** It shows each player the chance of getting caught *before* they commit to a bodge.

## Tech

| | |
|---|---|
| Engine | Unity 6 (6000.6), URP |
| Networking | Netcode for GameObjects 2.14, host-client. Join by room code over the internet (Unity Multiplayer Services) or by IP on a LAN |
| Authority | Host runs the job (budget, defects, inspector) via NetworkVariables and RPCs; each player drives their own bodger, and ownership of an item passes to whoever is carrying it |
| Input | Input System (keyboard & mouse, gamepads) |
| UI | uGUI + TextMeshPro, built from code |
| Physics | Velocity-driven carrying (held items still collide), throwable props, furniture that snaps in front of defects |
| Pipeline | Scenes, prefabs and materials built by editor scripts in [`Assets/Editor/ProjectBootstrap`](Assets/Editor/ProjectBootstrap); one palette file as the single source of colour ([`Palette.cs`](Assets/Scripts/Art/Palette.cs)) |

### Code map

```
Assets/Scripts
├── Job/          JobManager (budget, timer, phases), Defect, Inspector, RepairRules, PlayAreaGuard
├── Interaction/  CarryInteractor, Carryable, RepairKit, SupplyShelf, Concealer (furniture cover), Doorbell
├── Net/          NetSession (host / join by code / LAN), NetworkManagerGuard, NetSmokeTest
├── Players/      NetworkBodger (owner camera, hats by slot), BodgerInput, FirstPersonController, BodgerMotion
├── UI/           MainMenu (title, pause), PlayerHud (prompts, risk meter), JobHud (work order, help, report)
└── Art/          Palette (art bible)
```

## Running it

1. Open the project in **Unity 6000.6** (Unity Hub → Add → this folder).
2. Open `Assets/Scenes/SampleScene` and press **Play**.
3. **Host Game** gives a room code; friends press **Join** with it. On one network, **Host on LAN** + **Join by IP** work without an account.
4. To try 2–4 players on one machine: *Window → Multiplayer → Multiplayer Play Mode*, tick the extra players.

| Action | Keyboard | Gamepad |
|---|---|---|
| Move / look | WASD / mouse | Sticks |
| Buy, use, pick up, drop, hide behind | E | Y |
| Throw | Left mouse | X |
| Run / jump | Shift / Space | Stick press / A |
| How to play | Tab | Select |
| Pause / room code / leave | Esc | Start |
| Next job (on the report) | R | Start |

## Characters

<p align="center">
  <img src="Docs/Characters/p1_cap_toilet.jpg" alt="Bodger with a toilet" width="24%">
  <img src="Docs/Characters/p2_hardhat_planks.jpg" alt="Bodger with planks" width="24%">
  <img src="Docs/Characters/p3_goggles_roller.jpg" alt="Bodger with a paint roller" width="24%">
  <img src="Docs/Characters/p4_beanie_plant.jpg" alt="Bodger hiding behind a plant" width="24%">
</p>

Concept art. In game, the bodgers are blocky stand-ins that follow these designs until the 3D models are ready.

## Team

Made by **Mannapov Aibek**, **Kunbolov Alnur**, **Beisen Abylai** and **Daniyar Munsyzbayev**.

<div align="center">

**[▶ Bodge Squad on itch.io](https://aybekk.itch.io/bodge-squad)**

</div>
