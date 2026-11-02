# Tower circuit simulator — moved

The tower circuit simulator is now its own project: **TWR Trainer** (provisional name), repository
**ATCRoom/TWR-Trainer** (decided 2 Nov 2026). Its notes, decisions and to-do list are in `TODO.md` of that repository.

**Shared code (rule decided 2 Nov 2026):** the home of ATCRoom Map Creator and of the maps library (`MapCreator/`) is
ATCRoom/TWR-Trainer. **Before every new release of AuroraPAR v2 Trainer, copy `MapCreator/` from TWR-Trainer into this
repository.** The engine `Sim/Engine` was copied there and will change for the tower: a fix found in one is brought to
the other by hand when it applies.
