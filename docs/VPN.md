# Connecting through a VPN (draft)

> **Draft, written in advance (12 Oct 2026).** The direct connection by IP address is planned for the
> **AuroraPAR Instructor** and does not exist in the program yet. The VPN steps below are final; the
> AuroraPAR steps (marked *[to be confirmed]*) will be completed when the feature is built.

## When to use it

Normally the instructor and the student are linked through the **relays** with a session code: nothing to
install, nothing to configure. A VPN is the **alternative** when the relays are slow or not reachable:

| Situation | Link |
|---|---|
| Instructor and student on the **same PC** | direct, automatic |
| Same **home network** (same router) | direct, by IP address |
| Different places, relays working | relays + session code (default) |
| Different places, relays slow or blocked | **VPN** + IP address (this page) |

A VPN (Hamachi, Radmin VPN, ZeroTier, Tailscale…) puts the PCs on a **virtual private network**, as if they
were connected to the same router. AuroraPAR does not know about the VPN: it only needs the **IP address**
of the instructor. **Any VPN works**, as long as the two PCs can reach each other. The delay depends only on
the distance between the two PCs, so it is low and constant.

Both PCs must have the **same VPN program running** and be **in the same VPN network** during the session.

## Which VPN

| Program | Free for | Address of a PC | Notes |
|---|---|---|---|
| **Hamachi** (LogMeIn) | up to 5 PCs per network | `25.x.x.x` | many of us already have it |
| **Radmin VPN** (Famatech) | free, no user limit stated | `26.x.x.x` | recommended if Hamachi is not installed |
| ZeroTier | free plan | shown in the program | network created on the ZeroTier website |
| Tailscale | free for personal, non-commercial use (6 users) | `100.x.x.x` | sign in with a Google / Microsoft / GitHub account |

Free plans can change: check the program's site before relying on them.

## Hamachi, step by step

**Instructor (creates the network, once):**

1. Install Hamachi from [vpn.net](https://vpn.net/) and start it; press the **power button** (it turns blue).
2. **Network → Create a new network…**
3. Type a **Network ID** (a name, e.g. `ATCROOM-PAR`) and a **password**. **Create**.
4. Write down your Hamachi address: the number starting with **25.** at the top of the window
   (click on it → *Copy IPv4 address*).

**Student (joins, once):**

1. Install Hamachi and press the power button.
2. **Network → Join an existing network…**
3. Type the Network ID and the password given by the instructor. **Join**.
4. The instructor's PC appears in the list with a **green dot**: you are connected.

From then on, just start Hamachi before the session (the network is remembered).

## Radmin VPN, step by step

**Instructor (once):**

1. Install Radmin VPN from [radmin-vpn.com](https://www.radmin-vpn.com/) and start it.
2. **Create network**: type a **network name** and a **password**. **Create**.
3. Write down your address: the number starting with **26.** shown next to your PC name.

**Student (once):**

1. Install Radmin VPN and start it.
2. **Join network**: type the network name and the password. **Join**.
3. The instructor's PC appears under the network: you are connected.

Help pages of the program: [radmin-vpn.com/help](https://radmin-vpn.com/help/).

## Other VPNs (ZeroTier, Tailscale, …)

The idea is the same: both PCs in the same network, then the student uses the **instructor's address in
that network**, as shown by the VPN program.

## In AuroraPAR *[to be confirmed]*

1. **Instructor:** Instructor → *Session* → **Allow direct connections** (on). The window shows the port
   (default *[to be decided]*) and the addresses of this PC; the VPN one is the `25.…` (Hamachi) or
   `26.…` (Radmin VPN) address.
2. **Student:** *Connect to instructor* → **By IP address** → type the instructor's VPN address → **Connect**.
3. The status shows **Linked (direct)**.

## Windows firewall (the most common problem)

The first time the instructor allows direct connections, Windows asks whether AuroraPAR may communicate on
the networks: tick **both Private and Public** and press **Allow access**. VPN networks are often classed by
Windows as *Public*: if only *Private* is ticked, the student cannot connect.

If the question was answered wrongly: *Start → Windows Security → Firewall & network protection →
Allow an app through firewall → Change settings →* tick AuroraPAR for **Private** and **Public** → OK.

Antivirus programs with their own firewall (Kaspersky, Norton, ESET…) may need the same permission; for
Radmin VPN, Kaspersky suggests marking the `26.0.0.0/8` network as trusted.

## Troubleshooting

| Problem | What to check |
|---|---|
| The other PC is not in the VPN list / grey or red | both PCs: VPN program running and switched on, same network |
| PC listed, AuroraPAR does not connect | firewall on the **instructor** PC (above); right address (the VPN one, not the home one `192.168.…`) |
| Hamachi shows a yellow triangle | Hamachi could not make a direct tunnel: try again later or use Radmin VPN |
| Connection works, then drops | the VPN program was closed or the PC went to sleep |
| Network full (Hamachi) | free Hamachi networks take 5 PCs: use a second network or Radmin VPN |
