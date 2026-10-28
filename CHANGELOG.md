# Changelog

What changed in each version of Yata. The full notes (installing, checking the file and known limitations) are on [GitHub releases](https://github.com/akaneni/yata/releases), in Portuguese.

## 0.5.0 — 2026-10-28

- **Out of your calls.** Teams, Meet, Zoom, OBS and screenshots don't capture the notch, while you still see it: your next meeting, your tasks and your agent's project don't show up for others. On by default; it can be turned off in **Settings → General**.
- **The chime comes with the reminder.** A reminder that waited for a full-screen game chimes when it shows, not in the middle of the match. While your microphone is in use, or Windows asks not to be disturbed, reminders come without the chime. The chime can be turned off in **Settings → Notch notices**.
- **No late notices.** A reminder that waited and is no longer true, like "Standup in 5 min" after the meeting started, is dropped.
- **Welcome.** The first time, Settings open with what the notch can do and the choices that are yours. Water reminders now start off on new installs, and the tray menu has **Open Yata**.
- **Activities notch.** With the notch open, activities sit in a second notch beside it, each with a button to go to its window and one to dismiss it.
- **Ctrl+Alt+J** (off by default) goes to the window of what needs you and, pressed again, to the next one.
- **Safer updates.** Once Yata is code-signed, it only installs updates signed by the same publisher. The installer also checks Microsoft's signature before installing .NET.
- **Fixes:**
  - "ligar às 9h" (Portuguese for "call at 9") becomes a reminder;
  - **Join** recognizes Teams' new links and Outlook Safe Links;
  - a network hiccup no longer leaves a song without lyrics for 3 days;
  - the update button no longer gets stuck on "Downloading…";
  - an agent that works for a long time no longer drops out of the bubble.

[Full notes](https://github.com/akaneni/yata/releases/tag/v0.5.0)

## 0.4.0 — 2026-10-27

- **From the notch to the agent's terminal.** When Claude Code, Codex or a script needs you, a click on the bubble brings up the terminal or editor it runs in. You approve or deny in the tool itself: Yata never decides for the agent.
- **Settings → AI agents.** Turns the Claude Code, Cursor and Codex hooks on and off (WSL included) without editing files, backing up each config first.
- **Now, without waiting.** **Check now** looks for a new version, **↻** reads Google right away and **Update now** fetches the weather.
- The bubble no longer runs away from the mouse, and the notch opens when the mouse stops on it.

[Full notes](https://github.com/akaneni/yata/releases/tag/v0.4.0)

## 0.3.0 — 2026-10-27

- **A bubble beside the notch.** While music plays, the Pomodoro countdown and the next meeting move to a second, small notch, one thing at a time, by priority.
- **Activities from other programs.** `yata-cli` lets scripts, builds and downloads show their progress in the bubble.
- **AI agents in the notch.** Claude Code, Cursor and Codex show as working, waiting for you and done, one bubble per session. Only the state reaches the notch, never prompts, commands or answers.
- **Notices that aren't lost.** Reminders that come while the notch is open or in full screen wait and show one at a time.

[Full notes](https://github.com/akaneni/yata/releases/tag/v0.3.0)

## 0.2.1 — 2026-10-27

- **Update and restart.** With the new version notice on, updating takes one click: the installer comes from GitHub and is checked against its published SHA-256.
- **Google Tasks right away.** With the Tasks tab open, changes in Google Tasks show up within about 15 seconds.
- **Build provenance.** Each installer carries a record signed by GitHub that ties it to the code that built it.

[Full notes](https://github.com/akaneni/yata/releases/tag/v0.2.1)

## 0.2.0 — 2026-10-27

- **English and Portuguese**, following the Windows language or your choice.
- **Settings with a sidebar**, **Undo** when removing tasks or clearing the shelf, and choice menus.
- **Accessibility:** keyboard in Settings, names for screen readers, less motion when Windows asks for it, and more contrast.
- **A smaller installer:** from 6 MB to about 2.6 MB.

[Full notes](https://github.com/akaneni/yata/releases/tag/v0.2.0)

## 0.1.1 — 2026-10-26

- **Google Calendar and Google Tasks**, left out of 0.1.0.

[Full notes](https://github.com/akaneni/yata/releases/tag/v0.1.1)

## 0.1.0 — 2026-10-26

- First version: media from any player with synced lyrics, system notices, camera and microphone in use, Focus, Water, Tasks, Shelf, Weather, and Yata, the raven who brings your reminders.

[Full notes](https://github.com/akaneni/yata/releases/tag/v0.1.0)
