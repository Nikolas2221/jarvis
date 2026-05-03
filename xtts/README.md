# Jarvis XTTS sidecar

Optional local neural voice service for Jarvis Alpha.

It uses Coqui XTTS and a speaker reference wav to synthesize Russian speech.
Jarvis will try this service first when `voiceProvider` is `xtts` or `auto`.
If the service is not running, Jarvis falls back to the built-in online TTS.

## Setup

Copy a speaker sample to:

```text
xtts/speaker.wav
```

For example, you can use `jarvis_sample1.wav` from the Python Jarvis project.

XTTS requires Python 3.10 or 3.11. It does not install on Python 3.12+.

Recommended setup on Windows:

```powershell
cd xtts
.\install_xtts.ps1
```

Manual setup:

```powershell
cd xtts
py -3.11 -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
```

Run the server:

```powershell
.\.venv\Scripts\python.exe tts_server.py
```

Default URL:

```text
http://127.0.0.1:8765
```
