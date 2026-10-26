# Design notes: from a 2D escape room to VR

## Reference analysis
The reference (*Cyber Security Escape Room Online*) is a Unity WebGL point-and-click game set in one office. You click objects to zoom in, rotate them, pick them up and combine them. It has a "Game Information" card, a tutorial, 25 learning points, a progress-aware hint system, settings (quality, language, difficulty) and an end certificate. The laptop "browser" (mailbox / social / chat tabs) is a fake UI inside the game.

## VR translation
| 2D mechanic | VR version here |
|---|---|
| Click to zoom / rotate | Grab the object (direct or distance grab) and turn it in your hand. A caption bubble describes it. |
| Back button | Let go; physics drops it. Puzzle items respawn if they fall out of the world. |
| Inventory / combine | Physical sorting: carry items to the evidence box, shredder, quarantine container or waste bin. Wrong drops are rejected with an explanation. |
| HINT / gear buttons | Menu panel on Y/B: objectives, 3-tier hints, learning points, settings |
| In-game browser | World-space monitor UI (ray or poke) with tabs, an on-screen keyboard and a live password-policy checker |
| Timer | Exfiltration bar and countdown on the wall Containment Board |
| Final secret | Door code digits revealed per containment area, entered on a keypad |

## Story change
The reference is red-team ("steal Fred's file"). Ours is blue-team incident response: the same security lessons, but taught from the defender's side, ending with containment rather than theft.

## Interaction stack
Meta Interaction SDK through the Quick Actions API: the comprehensive OVR interaction rig (controllers and hands; poke, ray, grab, distance grab, teleport), `RayCanvasInteraction` and `PokeCanvasInteraction` on every UI canvas, `GrabInteraction` and `DistanceGrabInteraction` on items, and `RayInteractable` + `InteractableUnityEventWrapper` on point-to-use objects (briefcase lid, photo frame).

## Roadmap
More rooms (server room, reception), the red-team mode, Dutch/German localisation, an instructor statistics dashboard, and spatial-audio voice-over.
