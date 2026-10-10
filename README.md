<div align="center">

<img width="160" height="160" alt="EDCT" src="docs/logo.png" />

# EDCT
[![License](https://img.shields.io/badge/license-MIT-green?style=plastic)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows-blue?style=plastic)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/T3N624MRS4)

A Windows desktop tool for **Elite Dangerous** colonisation. Plan the structures in your systems, see exactly which commodities every build still needs, and watch the numbers count down live as you haul.

</div>

---

### What it does:
The app has two tabs and an in-game overlay:

- **System Plan:** Lay out each colony system body by body: which slot gets which structure, and which builds are active right now. The full build-cost catalogue is built in, including the higher-cost primary versions of each station. Start a new project from scratch, from a link to a colonisation planning Google Sheet, or from a Raven Colonial export.

- **Cargo Hauling:** One list of every commodity your active builds need, with how much is required, delivered, on your ship, on your fleet carrier, and still needed. The delivered, ship and carrier figures update by themselves from the game:
  - what the construction site has received, from the game's own depot reports
  - what's in your hold, from the game's cargo file
  - what you move to and from your own carrier, from your transfers

  It also works out how many trips are left at your ship's and your carrier's capacity, and shows which of your hub stations currently has the best stock of each commodity, so you know where to load next.

- **Overlay:** A small see-through window that sits on top of the game and shows only what's still needed, or where you're unloading. Lock it so clicks go straight through to the game, set its colours and transparency, and show, hide or lock it with your own hotkeys while you play.

If the numbers ever look off, **Rescan Journal** reads the current session's journal again from the start.

### Where the data comes from:
- **Build costs** come from the community colonisation planning sheet (Colonization Construction v3 by DaftMav) and SrvSurvey's colonisation cost data.
- **Your progress** comes from your own game journal and cargo file, read on your PC.
- **Hub station stock** comes from the same EDDN-fed relay that EDCF uses. Please run a journal reporting tool such as [EDDiscovery](https://github.com/EDDiscovery/EDDiscovery) or [EDMarketConnector](https://github.com/EDCD/EDMarketConnector) to help keep that data fresh for everyone.

### Requirements:
- **Windows 10 or 11.** It doesn't run on macOS or Linux.
- **Elite Dangerous** on the same PC. The journal folder is found automatically.
- Internet access for the hub station prices and sheet imports. Everything else works offline.

### Installing:
Run `ColonyTracker.exe` with the `Data` folder next to it. Your projects and settings are saved in that same folder. There's no installer and nothing else to set up, it's a single self-contained program.

### Known limitations:
- Transfers to and from a squadron carrier can't be tracked automatically, because the game doesn't write them to the journal. Only your own carrier is tracked.
- Carrier stock picked up from your transfers is saved when the app closes normally. If it crashes, that session's transfers are lost.

---

### Disclaimer

EDCT is an independent, unofficial project and is not affiliated with, endorsed by, or sponsored by Frontier Developments plc. Elite Dangerous and all related trademarks are the property of Frontier Developments plc.

This software is provided "as is," without warranty of any kind, express or implied. Use at your own risk.
