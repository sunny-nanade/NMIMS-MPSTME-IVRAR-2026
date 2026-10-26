# How to play - Cyber Security Escape Room VR (story mode)

Three chapters, each a guided incident-response scene. A green beacon marks the next step, the wrist panel shows the current objective, and Maya narrates.

## Controls (Quest controllers / Meta XR Simulator)
| Action | Controller |
|---|---|
| Point at a card / button | Aim the ray from either controller |
| Click | Trigger |
| Grab an item | Grip (near) - or point and grip to pull it to your hand (distance grab) |
| Drop | Release grip over a drop zone (bin, shredder, evidence bag, etc.) |
| Teleport | Push the thumbstick forward, aim at a hotspot, release |
| Turn | Thumbstick left / right |
| Menu (objectives, hints, learning points, settings) | Y or B (or Start); M on a keyboard |
| Knock on / open a door (main-menu stairwell, badge reader in Ch1, override code in Ch2, exit in Ch3) | P on a keyboard, standing next to the door; the next chapter starts a few seconds after it opens |
| Flashlight on/off (Chapter 3, after you pick it up) | F on a keyboard |
| Hint | HINT button on the wrist panel (tiers get more specific) |

## Chapter 1 - Night Shift (SOC)
1. Press **Begin** on the intro card.
2. Pick up your access badge from your desk.
3. Open the critical alert on the SIEM console, find the compromised host in the logs, and classify the attack.
4. Declare a critical incident.
5. Badge through the door to Fred's office (hold the badge to the reader).

## Chapter 2 - Fred's Office
1. Kill the remote session on Fred's workstation (lock it).
2. Evidence: bag the printout and the note. Shred the confidential documents. Quarantine the USB stick.
3. Sign in to Fred's account on the workstation (use the keyboard on its screen), set a strong new password, turn on 2FA.
4. Report every phishing email; handle the "IT Helpdesk" chat (do not give out credentials).
5. Encrypt Project AURORA and revoke the attacker.
6. Enter the override code (found in the room) on the door keypad.

## Chapter 3 - The Server Room
1. Find the rogue device in the racks, pull its network cable, bag it as evidence.
2. Move the infected port to the quarantine VLAN.
3. Restore the file server from a clean backup.
4. File the incident report.

## Scenarios
- **Wrong drop zone:** the item is rejected, a hint explains why, and it is set back beside the zone.
- **Item falls or is lost:** it returns to its starting place.
- **Stuck:** open the menu, read the objective, press HINT (up to three tiers).
- **Timed chapters:** pick Normal or Expert on the intro card; running out of time shows the fail card with a retry.
- **Win:** the chapter-complete card summarises the learning points; chapter 3 ends with a certificate.

## Inventory, flashlight, badge, laptop (new)
- **Inventory:** press **I** (keyboard) or **X** (left controller), or the **BAG** button on your wrist. It has 4 slots. Hold an item, open the bag, press *Store*. *Take out* puts it in front of you. When the bag is full you must pick one item to *Throw out* (it lands on the floor, nothing is destroyed).
- **Flashlight (Chapter 3):** grabbing it straps it to your wrist, pointing where your hand points. **F** or a right-stick click switches it on/off.
- **Badge (Chapter 1):** it stays where you let go of it. Touch it to the reader by the door once the laptop is clean and the incident is declared: the next chapter starts by itself.
- **Infected laptop (Chapter 1):** the laptop on the left desk shows *SECURITY BREACH DETECTED*. After declaring the incident: terminate the unsigned process, disconnect from the network, quarantine the file.
