// De kaart van de Planning met de Azure Maps Web SDK (ADR 0006). De punten staan in de data-attributen van het element:
// data-bedrijf="breedte,lengte" en data-stops='[{"nummer","klant","ronde":"levering"|"ophaling","punt":"breedte,lengte"}]'.

const sdkVersion = "3";
const colors = { levering: "#1565c0", ophaling: "#ef6c00", bedrijf: "#424242", speld: "#c62828" };
const maps = new Map();
let sdk;

/** Laadt de SDK één keer, pas als er een kaart getoond wordt. */
function loadSdk() {
    sdk ??= new Promise((resolve, reject) => {
        const css = document.createElement("link");
        css.rel = "stylesheet";
        css.href = `https://atlas.microsoft.com/sdk/javascript/mapcontrol/${sdkVersion}/atlas.min.css`;
        document.head.appendChild(css);
        const script = document.createElement("script");
        script.src = `https://atlas.microsoft.com/sdk/javascript/mapcontrol/${sdkVersion}/atlas.min.js`;
        script.onload = () => resolve(window.atlas);
        script.onerror = () => { sdk = undefined; reject(new Error("Azure Maps laadt niet.")); };
        document.head.appendChild(script);
    });
    return sdk;
}

/** "51.17,5.17" naar een positie van Azure Maps: eerst de lengtegraad. */
function position(text) {
    if (!text) return null;
    const [latitude, longitude] = text.split(",").map(Number);
    return Number.isFinite(latitude) && Number.isFinite(longitude) ? [longitude, latitude] : null;
}

function escape(text) {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
}

export async function show(id, key, dotnet) {
    const element = document.getElementById(id);
    if (!element) return;
    const atlas = await loadSdk();
    let state = maps.get(id);
    if (state && state.element !== element) {
        state.map.dispose();
        state = undefined;
    }
    if (!state) {
        const map = new atlas.Map(element, {
            authOptions: { authType: "subscriptionKey", subscriptionKey: key },
            language: "nl-NL",
            center: [4.4, 51.0],
            zoom: 8,
        });
        state = { element, map, atlas, dotnet, pinning: false, pin: null, ready: new Promise(r => map.events.add("ready", r)) };
        maps.set(id, state);
        map.events.add("click", e => { if (state.pinning) place(state, e.position); });
    }
    state.dotnet = dotnet;
    await state.ready;
    draw(state);
}

function draw(state) {
    const { atlas, map, element } = state;
    map.markers.clear();
    map.popups.clear();
    if (state.pin) map.markers.add(state.pin);
    const positions = [];

    function marker(at, color, text, label) {
        const popup = new atlas.Popup({ content: `<div class="planning-map-popup">${escape(label)}</div>`, pixelOffset: [0, -30] });
        const m = new atlas.HtmlMarker({ position: at, color, text, popup });
        map.markers.add(m);
        map.events.add("click", m, () => m.togglePopup());
        positions.push(at);
    }

    const business = position(element.dataset.bedrijf);
    if (business) marker(business, colors.bedrijf, "", "Vertrek en aankomst");
    const counters = { levering: 0, ophaling: 0 };
    for (const stop of JSON.parse(element.dataset.stops || "[]")) {
        const at = position(stop.punt);
        if (!at) continue;
        counters[stop.ronde] = (counters[stop.ronde] ?? 0) + 1;
        const round = stop.ronde === "levering" ? "Levering" : "Ophaling";
        marker(at, colors[stop.ronde] ?? colors.levering, String(counters[stop.ronde]), `${round}: ${stop.klant} (${stop.nummer})`);
    }

    if (positions.length > 1)
        map.setCamera({ bounds: atlas.data.BoundingBox.fromPositions(positions), padding: 60 });
    else if (positions.length === 1)
        map.setCamera({ center: positions[0], zoom: 13 });
}

/** Zet de speld waar geklikt werd; ze blijft versleepbaar tot ze bevestigd of geannuleerd wordt. */
function place(state, at) {
    if (!state.pin) {
        state.pin = new state.atlas.HtmlMarker({ position: at, color: colors.speld, draggable: true });
        state.map.markers.add(state.pin);
        state.map.events.add("dragend", state.pin, () => report(state));
    } else {
        state.pin.setOptions({ position: at });
    }
    report(state);
}

function report(state) {
    const [longitude, latitude] = state.pin.getOptions().position;
    state.dotnet.invokeMethodAsync("PinPlaced", latitude, longitude);
}

export function pin(id, on) {
    const state = maps.get(id);
    if (!state) return;
    state.pinning = on;
    state.map.getCanvasContainer().style.cursor = on ? "crosshair" : "";
    if (!on && state.pin) {
        state.map.markers.remove(state.pin);
        state.pin = null;
    }
}

export function dispose(id) {
    maps.get(id)?.map.dispose();
    maps.delete(id);
}
