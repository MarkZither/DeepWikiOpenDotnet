# WSL2 Mirrored Networking Mode

## The Problem

WSL2 uses NAT networking by default. The Windows host is reachable from inside WSL via a gateway IP
that is assigned dynamically each time the WSL2 VM starts — and it changes on Windows reboot. If you
run services on your Windows host (such as Ollama for local LLM inference) and need to reach them
from inside WSL, you end up hardcoding an IP like `172.26.32.1` that silently breaks after the next
restart.

---

## The Solution: `networkingMode=mirrored`

Introduced in **Windows 11 22H2 (September 2022)**, mirrored networking mode makes WSL2 share the
Windows host's network stack directly rather than sitting behind a NAT. The result: `localhost` and
`127.0.0.1` inside WSL resolve to the same interfaces as on Windows, permanently and stably.

### Enabling It

Create or edit `C:\Users\<YourUsername>\.wslconfig`:

```ini
[wsl2]
networkingMode=mirrored
```

Then restart WSL from PowerShell:

```powershell
wsl --shutdown
```

After WSL restarts, any Windows service bound to `0.0.0.0` (including Ollama) is reachable at
`http://localhost:<port>` from inside WSL — no IP hunting required.

---

## Pros

| Benefit | Detail |
|---|---|
| **Stable host address** | `localhost` always works, survives reboots |
| **No port forwarding layer** | WSL services are reachable on `localhost` from Windows too |
| **IPv6 works** | Full IPv6 support, unlike NAT mode |
| **Multicast/broadcast** | mDNS and service discovery work correctly |
| **Tailscale integration** | Tailscale IPs are reachable natively from WSL; no separate daemon or proxy needed |
| **VPN inheritance** | WSL inherits Windows routing rules including split-tunnel configuration — traffic goes through the right interfaces automatically |
| **`DOCKER_HOST` reliability** | If you use `DOCKER_HOST` on Windows to reach Docker Engine running in WSL, the connection goes through a shared loopback rather than WSL's NAT port forwarding, which is more stable |

## Cons / Caveats

| Caveat | Who It Affects |
|---|---|
| **Docker Desktop conflicts** | Docker Desktop's WSL2 backend hooks network at a low level and can fight with mirrored mode, breaking container internet access. **Not relevant if you run Docker Engine natively in WSL.** |
| **Windows 11 22H2+ only** | Not available on Windows 10 |
| **VPN inheritance** | A downside if you *don't* want WSL to go through your VPN — in NAT mode WSL bypasses it. A feature if you *do* want it. |
| **Port collision** | Windows and WSL services both binding `0.0.0.0:PORT` now conflict. In NAT mode they were on separate interfaces. |
| **Still a "preview" feature** | Microsoft's changelog marks it preview, though it is stable in practice for the use cases above |

---

## Why It Suits This Setup

This machine runs:

- **Docker Engine** natively inside WSL (not Docker Desktop)
- **Podman** inside WSL
- **Docker CLI on Windows** using `DOCKER_HOST` to reach the WSL daemon
- **Ollama** on the Windows host, accessed from ASP.NET services running in WSL
- **Tailscale + NordVPN** on Windows with split tunnelling

Every one of these benefits from mirrored mode:

- Docker Engine's `docker0` bridge lives entirely inside WSL and is unaffected by the host networking mode change
- `DOCKER_HOST` on Windows becomes more reliable with a shared loopback
- Ollama is permanently at `localhost:11434` — no config changes needed after reboots
- Tailscale peers are reachable from WSL without running a second daemon
- NordVPN's split-tunnel rules apply to WSL automatically, which is the desired behaviour

The one canonical reason *not* to use mirrored mode — Docker Desktop WSL backend conflicts — does not
apply here because Docker Desktop is intentionally not installed.
