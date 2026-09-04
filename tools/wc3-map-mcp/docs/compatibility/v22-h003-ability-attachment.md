# v22 H003 ability attachment fix

The v21 runtime report showed that H003 purchased successfully but its ability
bar was empty. The cause was the Warcraft III unit object field split: v21 put
the four hero skills in `uabi` (normal abilities) while clearing `uhab` (hero
abilities). v22 keeps the inventory ability in `uabi` and places
`A2Q1,A2W1,A2E1,A2R1` in `uhab`, which is the hero skill list consumed by the
in-game hero ability bar.

The altar stock remains `H001,H002,H003,H004`; H001, H002, and H004 remain
without the Controller kit. The source map and v20 artifact remain immutable.
Runtime behavior for v22 remains a manual Warcraft III verification gate.
