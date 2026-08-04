SCHEDULE I BACKPACK DATABASE - WINDOWS SERVER 2025
==================================================

This is the correct package for the Windows Server 2025 VPS shown in your
screenshot. Do not use the Ubuntu/Debian VPS package on this machine.

WHAT IT INSTALLS
----------------
- PostgreSQL 18, as a native Windows service
- Python 3.13 and an isolated Backpack API environment
- Caddy, as a native Windows HTTPS service
- A startup task that automatically restarts the API after failure/reboot
- Windows Firewall rules for HTTPS only (TCP 80/443 and UDP 443)

PostgreSQL (5432) and the API (8080) listen on localhost only. They are not
exposed directly to the internet. No Docker, WSL, Hyper-V or reboot is needed.

BEFORE SETUP
------------
1. Choose either the VPS public IPv4 address or a domain/subdomain. A domain is
   optional. If using a domain, point its DNS A record to the VPS public IPv4.
2. Make sure your VPS provider's firewall/security group permits inbound TCP
   ports 80 and 443. The script also configures Windows Firewall.
3. Confirm the VPS has a dedicated public IPv4 address. Private addresses such
   as 10.x, 172.16-31.x and 192.168.x cannot be used remotely.
4. Know your 17-digit SteamID64.
5. Copy this entire folder onto the VPS. Do not run it from your gaming PC.

INSTALL
-------
1. Sign in to the VPS through Remote Desktop.
2. Right-click SETUP-WINDOWS-VPS.bat and choose Run as administrator.
3. Enter either the public IPv4 address or hostname, plus the certificate email
   and SteamID64, when asked.
4. Type INSTALL after ports 80/443 can reach the VPS. For domain mode, also wait
   until its DNS record points to the VPS.
5. At completion, copy PLAYER-TOKEN-<SteamID64>.txt somewhere safe, then delete
   unsecured copies after entering the token into that player's Companion.
6. Run CHECK-STATUS.bat. The first HTTPS certificate can take a minute.

The installers are downloaded from the official PostgreSQL/EDB, Python and
Caddy sites. Signed EXE installers are publisher-verified before execution.

IP-ADDRESS HTTPS
----------------
Public IP certificates are supported without a domain. The installer explicitly
requests Let's Encrypt's short-lived certificate profile. IP certificates last
about six days, so Caddy must remain running to renew them automatically. The
Companion URL will be https://YOUR.VPS.IP with no certificate warning.

INTERRUPTED SETUP RECOVERY
--------------------------
If setup previously stopped immediately after "Installing PostgreSQL 18", run
the current setup package again. When it detects PostgreSQL, type RESET at the
recovery prompt. The script temporarily permits only localhost recovery, assigns
a new random administrator password, restores authentication immediately, and
continues installation. Do not use RESET for a separately managed PostgreSQL
installation unless you deliberately want its postgres password replaced.

The setup also checks and starts the built-in Windows Installer service before
installing Python. If registration is damaged, it attempts the standard local
re-registration and writes Python's detailed log under the current Administrator
account's TEMP\ScheduleIBackpackSetup folder.

DAILY TOOLS
-----------
ADD-PLAYER.bat       Creates a separate private token for another player.
REVOKE-PLAYER.bat    Invalidates all tokens belonging to one SteamID64.
CHECK-STATUS.bat     Checks services, local API, public HTTPS and bind addresses.
BACKUP-DATABASE.bat  Creates a dated database backup in the Backups folder.
RESTORE-DATABASE.bat Restores a backup after an explicit warning.
UPDATE-SERVER.bat    Updates this package's API files and Python dependencies.
UNINSTALL-SERVER.bat Disables safely, or permanently removes backpack data.
START-SERVER.bat     Re-enables a server previously disabled by uninstall option 1.

IMPORTANT SECURITY NOTES
------------------------
- Never open ports 5432 or 8080 in a firewall or router.
- Never share a player token publicly or use the same token for two people.
- Back up regularly and copy backups off the VPS.
- The VPS screenshot shows an Evaluation edition with 179 days remaining.
  Installation works normally, but activate/license Windows Server before that
  evaluation expires. Run `slmgr /xpr` in an Administrator Command Prompt to
  check the expiry; this kit does not alter Windows licensing.

The hosted service is an additional recovery mirror. Multiplayer item movement
remains host-authoritative and each backpack remains keyed to its owner's
SteamID64 plus the current career/save identity.
