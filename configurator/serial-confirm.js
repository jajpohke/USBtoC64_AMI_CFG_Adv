// USB2C64 web configurator: send a JSON command to the adapter and wait for its answer.
//
// Every save command answers {"status":"ok"} when the data is stored (firmware 1.2 and 1.2.3).
// When the command is lost or rejected, firmware 1.2 answers nothing and firmware 1.2.3 answers
// {"status":"error",...}. The pages use the answer to say SAVED / NOT SAVED / NOT CONFIRMED.
//
// The command is sent in small chunks: the adapter's USB receive buffer is 256 bytes on firmware 1.2,
// and a long gamepad profile sent in one go arrived truncated (and was silently ignored).
const USB2C64 = (() => {
    const CHUNK = 32;      // bytes per write
    const PAUSE_MS = 15;   // pause between chunks

    async function write(port, text) {
        const writer = port.writable.getWriter();
        try {
            const bytes = new TextEncoder().encode(text);
            for (let i = 0; i < bytes.length; i += CHUNK) {
                await writer.write(bytes.slice(i, i + CHUNK));
                if (i + CHUNK < bytes.length) await new Promise(r => setTimeout(r, PAUSE_MS));
            }
        } finally {
            writer.releaseLock();
        }
    }

    // First {...} object with a "status" field in the received text (other output is ignored).
    function findStatus(text) {
        const re = /\{[^{}]*\}/g;
        let m;
        while ((m = re.exec(text)) !== null) {
            try {
                const o = JSON.parse(m[0]);
                if (o && typeof o.status === "string") return o;
            } catch (e) { /* not JSON */ }
        }
        return null;
    }

    // Returns { result: "ok" } | { result: "error", reason, detail } | { result: "none" } (no answer in time).
    async function sendAndConfirm(port, payload, timeoutMs = 4000) {
        await write(port, JSON.stringify(payload) + "\n");

        const reader = port.readable.getReader();
        const decoder = new TextDecoder();
        let incoming = "";
        let found = null;
        let finished = false;
        const readPromise = (async () => {
            try {
                while (true) {
                    const { value, done } = await reader.read();
                    if (done) break;
                    incoming += decoder.decode(value, { stream: true });
                    found = findStatus(incoming);
                    if (found) break;
                }
            } catch (e) { /* port closed (e.g. the adapter rebooted) */ }
            finished = true;
        })();
        await Promise.race([readPromise, new Promise(r => setTimeout(r, timeoutMs))]);
        if (!finished) { try { await reader.cancel(); } catch (e) { } }
        try { reader.releaseLock(); } catch (e) { }

        if (!found) return { result: "none" };
        if (found.status === "ok") return { result: "ok" };
        return { result: "error", reason: found.reason || found.status, detail: found.detail || "" };
    }

    // Full-page result screen (same style as the existing "SAVED" screens).
    function resultScreen(kind, title, lines) {
        const color = kind === "ok" ? "var(--lcars-green,#32CD32)"
                    : kind === "error" ? "var(--lcars-red,#FF3B30)" : "var(--lcars-amber,#FFC107)";
        const body = lines.map(l =>
            `<div style="color:#bbb; font-size:1em; margin-bottom:12px; font-family:sans-serif; text-transform:none; max-width:720px;">${l}</div>`
        ).join("");
        document.body.innerHTML = `
            <div style="
                display:flex; flex-direction:column; align-items:center; justify-content:center;
                min-height:100vh; background:#000; font-family:'Oswald',sans-serif;
                text-transform:uppercase; text-align:center; padding:40px; box-sizing:border-box;">
                <div style="color:${color}; font-size:2.2em; font-weight:700; letter-spacing:0.1em; margin-bottom:16px;">
                    ${title}
                </div>
                ${body}
                <button onclick="location.reload()" style="
                    margin-top:28px; background:${color}; color:#000; border:none;
                    border-radius:25px; padding:14px 48px; font-family:'Oswald',sans-serif;
                    font-size:1.2em; font-weight:700; cursor:pointer; letter-spacing:0.08em;
                    text-transform:uppercase;">
                    🔄 REFRESH PAGE
                </button>
            </div>`;
    }

    const UPDATE_HINT = "Firmware 1.2 can lose long settings without telling. Update the adapter to firmware 1.2.3 " +
        "with the <a href=\"https://github.com/jajpohke/USBtoC64_AMI_CFG_Adv/blob/main/WINDOWSTOOL.md\" target=\"_blank\" " +
        "rel=\"noopener\" style=\"color:#99CCFF\">Windows Config Tool</a>.";

    const esc = t => String(t).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));

    function errorText(r) {
        return "The adapter rejected the data" + (r.reason ? ` (${esc(r.reason)}${r.detail ? ": " + esc(r.detail) : ""})` : "") +
               ". Nothing was changed: please try again.";
    }

    return { write, sendAndConfirm, resultScreen, errorText, UPDATE_HINT };
})();
