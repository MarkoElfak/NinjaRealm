# Ninja Realm

A 3D low poly auto battler (Teamfight Tactics style fights, Ninja Warz style persistent roster and village) made in Unity 6000.4.6f1 with URP.

* Read `docs/ARCHITECTURE.md` before changing code. Content is data (ScriptableObjects with stable IDs); systems are code.
* `NinjaRealm.Combat` must stay pure C# (`noEngineReferences: true`) and deterministic: use `DeterministicRandom`, integer basis points for chances, and ordered iteration.
* Commit `.meta` files with every new asset, script or folder, and keep each GUID stable.
* Tests live in `Assets/_Project/Tests/EditMode` and run with the Unity Test Runner (EditMode).
* Git flow: branch from `develop`, open a pull request into `develop`, and the owner reviews and merges. Never push to `main` or `develop` directly.
