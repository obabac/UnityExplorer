# Deterministic Unity Test Client Candidates

- **Unity Space Shooter (Unity Technologies/space-shooter-tutorial)**  
  Determinism: wave/timeline-driven, no RNG by default. Includes UI, physics, particles, audio. Light build (2019–2021 LTS proven).
  Current usage: validated on Test-VM with IL2CPP x86_64 + MelonLoader 0.7.2-ci (nightly) + UnityExplorer 4.12.8; UI currently broken but MCP runs headless via a small patch mod.

- **Unity Tanks! (Unity Technologies/tanks-tutorial)**  
  Determinism: fixed map/spawns; power-up timing can be set static. Covers UI canvases, camera rigs, physics collisions.

- **Unity Karting Microgame (Unity Technologies/karting-microgame)**  
  Determinism: single track, spline AI; item box randomness can be disabled for repeatable laps. Good physics coverage.

- **Custom deterministic harness build (bespoke)**  
  Determinism: scripted timeline (spawns/UI toggles/scene swaps) with a fixed seed exposed via CLI arg. Tiny footprint tailored to UnityExplorer/MCP coverage.
