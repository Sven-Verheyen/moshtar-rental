// Bewerkt de tekst van een mailsjabloon op de plaats van de cursor. Blazor zet de nieuwe waarde terug in het veld.

function textarea(id) {
    return document.getElementById(id)?.querySelector("textarea") ?? null;
}

export function wrap(id, before, after, example) {
    const el = textarea(id);
    if (!el) return null;
    const { selectionStart: start, selectionEnd: end, value } = el;
    const selected = value.slice(start, end) || example;
    return {
        value: value.slice(0, start) + before + selected + after + value.slice(end),
        start: start + before.length,
        end: start + before.length + selected.length,
    };
}

export function insert(id, text) {
    const el = textarea(id);
    if (!el) return null;
    const { selectionStart: start, selectionEnd: end, value } = el;
    return { value: value.slice(0, start) + text + value.slice(end), start: start + text.length, end: start + text.length };
}

export function select(id, start, end) {
    const el = textarea(id);
    if (!el) return;
    el.focus();
    el.setSelectionRange(start, end);
}
