import io
import os
import wave

import numpy as np
from flask import Flask, jsonify, request, send_file
from TTS.api import TTS


HOST = os.environ.get("JARVIS_XTTS_HOST", "127.0.0.1")
PORT = int(os.environ.get("JARVIS_XTTS_PORT", "8765"))
MODEL_PATH = os.environ.get("JARVIS_XTTS_MODEL", "./models/tts_models--multilingual--multi-dataset--xtts_v2")
SPEAKER_WAV = os.environ.get("JARVIS_XTTS_SPEAKER", "./speaker.wav")
DEVICE = os.environ.get("JARVIS_XTTS_DEVICE", "cuda")
LANGUAGE = os.environ.get("JARVIS_XTTS_LANGUAGE", "ru")
SAMPLE_RATE = int(os.environ.get("JARVIS_XTTS_SAMPLE_RATE", "24000"))

app = Flask(__name__)
tts = None


def load_tts():
    global tts
    if tts is not None:
        return tts

    if not os.path.exists(SPEAKER_WAV):
        raise FileNotFoundError(f"Speaker wav not found: {SPEAKER_WAV}")

    config_path = os.path.join(MODEL_PATH, "config.json")
    if os.path.exists(config_path):
        tts = TTS(model_path=MODEL_PATH, config_path=config_path).to(DEVICE)
    else:
        tts = TTS("tts_models/multilingual/multi-dataset/xtts_v2").to(DEVICE)
    return tts


def wav_bytes(samples, sample_rate):
    data = np.asarray(samples, dtype=np.float32)
    data = np.clip(data, -1.0, 1.0)
    pcm = (data * 32767.0).astype(np.int16)

    buf = io.BytesIO()
    with wave.open(buf, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(pcm.tobytes())
    buf.seek(0)
    return buf


@app.get("/health")
def health():
    return jsonify({"ok": True, "loaded": tts is not None})


@app.post("/speak")
def speak():
    payload = request.get_json(force=True)
    text = (payload.get("text") or "").strip()
    if not text:
        return jsonify({"error": "text is required"}), 400

    model = load_tts()
    samples = model.tts(
        text,
        speaker_wav=SPEAKER_WAV,
        language=LANGUAGE,
        split_sentences=False,
    )
    return send_file(wav_bytes(samples, SAMPLE_RATE), mimetype="audio/wav")


if __name__ == "__main__":
    print(f"Jarvis XTTS server: http://{HOST}:{PORT}")
    app.run(host=HOST, port=PORT, threaded=False)
