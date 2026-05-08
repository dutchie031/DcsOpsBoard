window.dcsMap = window.dcsMap || {};

window.dcsMap._state = {
    map: null,
    activeMap: "caucasus",
    detailedSource: null,
    coverageByMap: new Map()
};

window.dcsMap.configureDetailedLayerLookup = function(map) {
    const state = window.dcsMap._state;
    state.map = map;

    const detailedLayer = map.getAllLayers().find((l) => l.get("id") === "detailed-layer");
    if (!detailedLayer) {
        console.warn("dcsMap: detailed-layer not found");
        return;
    }

    const source = detailedLayer.getSource();
    if (!source || typeof source.setTileUrlFunction !== "function") {
        console.warn("dcsMap: detailed-layer source does not support setTileUrlFunction");
        return;
    }

    state.detailedSource = source;

    source.setTileUrlFunction(function(tileCoord) {
        if (!tileCoord) return undefined;

        const z = tileCoord[0];
        const x = tileCoord[1];
        const y = tileCoord[2];

        if (z < 15 || z > 17) return undefined;

        const coverage = state.coverageByMap.get(state.activeMap);
        if (!coverage) return undefined;

        if (!window.dcsMap.isCovered(coverage, z, x, y)) return undefined;

        // Adjust path to your real endpoint.
        return `/api/detailed-tiles/${state.activeMap}/${z}/${x}/${y}.webp`;
    });

    window.dcsMap.refreshDetailedSource();
};

window.dcsMap.setActiveMap = async function(mapName) {
    const state = window.dcsMap._state;
    state.activeMap = (mapName || "unknown").toLowerCase();

    await window.dcsMap.ensureCoverageLoaded(state.activeMap);
    window.dcsMap.refreshDetailedSource();
};

window.dcsMap.ensureCoverageLoaded = async function(mapName) {
    const state = window.dcsMap._state;
    if (state.coverageByMap.has(mapName)) return;

    try {
        // Adjust path to where you serve coverage JSON.
        const res = await fetch(`/api/detailed-coverage/${mapName}/coverage.json`, { cache: "force-cache" });
        if (!res.ok) throw new Error(`coverage fetch failed: ${res.status}`);
        const json = await res.json();
        state.coverageByMap.set(mapName, json);
    } catch (err) {
        console.warn("dcsMap: failed to load coverage", mapName, err);
        // Cache empty coverage to avoid repeated failed fetches.
        state.coverageByMap.set(mapName, {});
    }
};

window.dcsMap.isCovered = function(coverage, z, x, y) {
    // Expected shape:
    // {
    //   "15": ["17234:11405", "17235:11405"],
    //   "16": ["34469:22811"]
    // }
    const level = coverage[String(z)];
    if (!Array.isArray(level)) return false;
    const key = `${x}:${y}`;
    return level.includes(key);
};

window.dcsMap.refreshDetailedSource = function() {
    const source = window.dcsMap._state.detailedSource;
    if (!source) return;

    if (typeof source.clear === "function") {
        source.clear();
    }
    source.changed();
};