window.dcsMap = window.dcsMap || {};

window.dcsMap._state = {
    map: null,
    activeMap: "caucasus",
    detailedSource: null,
    buildupSource: null,
    objectsSource: null,
    detailedCoverageByMap: new Map(),
    buildupCoverageByMap: new Map(),
    objectsCoverageByMap: new Map()
};

window.dcsMap.configureDetailedLayerLookup = function(map) {
    const state = window.dcsMap._state;
    state.map = map;

    // --- detailed raster layer ---
    const detailedLayer = map.getAllLayers().find((l) => l.get("id") === "detailed-layer");
    if (detailedLayer) {
        const source = detailedLayer.getSource();
        if (source && typeof source.setTileUrlFunction === "function") {
            state.detailedSource = source;
            source.setTileUrlFunction(function(tileCoord) {
                if (!tileCoord) return undefined;
                const z = tileCoord[0], x = tileCoord[1], y = tileCoord[2];
                if (z < 15 || z > 17) return undefined;
                const coverage = state.detailedCoverageByMap.get(state.activeMap);
                if (!coverage || !window.dcsMap.isCovered(coverage, z, x, y)) return undefined;
                return `/api/detailed-tiles/${state.activeMap}/${z}/${x}/${y}.webp`;
            });
        } else {
            console.warn("dcsMap: detailed-layer source does not support setTileUrlFunction");
        }
    } else {
        console.warn("dcsMap: detailed-layer not found");
    }

    // --- buildup MVT layer ---
    const buildupLayer = map.getAllLayers().find((l) => l.get("id") === "buildup-layer");
    if (buildupLayer) {
        const source = buildupLayer.getSource();
        if (source && typeof source.setTileUrlFunction === "function") {
            state.buildupSource = source;
            source.setTileUrlFunction(function(tileCoord) {
                if (!tileCoord) return undefined;
                const z = tileCoord[0], x = tileCoord[1], y = tileCoord[2];
                const coverage = state.buildupCoverageByMap.get(state.activeMap);
                if (!coverage || !window.dcsMap.isCovered(coverage, z, x, y)) return undefined;
                return `/api/buildup/${state.activeMap}/${z}/${x}/${y}.mvt`;
            });
        } else {
            console.warn("dcsMap: buildup-layer source does not support setTileUrlFunction");
        }
    } else {
        console.warn("dcsMap: buildup-layer not found");
    }

    // --- objects MVT layer ---
    const objectsLayer = map.getAllLayers().find((l) => l.get("id") === "object-layer");
    if (objectsLayer) {
        const source = objectsLayer.getSource();
        if (source && typeof source.setTileUrlFunction === "function") {
            state.objectsSource = source;
            source.setTileUrlFunction(function(tileCoord) {
                if (!tileCoord) return undefined;
                const z = tileCoord[0], x = tileCoord[1], y = tileCoord[2];
                if (z < 15 || z > 17) return undefined;
                const coverage = state.objectsCoverageByMap.get(state.activeMap);
                if (!coverage || !window.dcsMap.isCovered(coverage, z, x, y)) return undefined;
                return `/api/objects/${state.activeMap}/${z}/${x}/${y}.mvt`;
            });
        } else {
            console.warn("dcsMap: object-layer source does not support setTileUrlFunction");
        }
    } else {
        console.warn("dcsMap: object-layer not found");
    }

    window.dcsMap.refreshAllSources();
};

window.dcsMap.setActiveMap = async function(mapName) {
    const state = window.dcsMap._state;
    state.activeMap = (mapName || "unknown").toLowerCase();
    console.log("dcsMap: active map set to", state.activeMap);
    await Promise.all([
        window.dcsMap.ensureCoverageLoaded("detailed", state.activeMap),
        window.dcsMap.ensureCoverageLoaded("buildup", state.activeMap),
        window.dcsMap.ensureCoverageLoaded("objects", state.activeMap)
    ]);

    window.dcsMap.refreshAllSources();
};

window.dcsMap.ensureCoverageLoaded = async function(type, mapName) {
    const state = window.dcsMap._state;
    const storeKey = `${type}CoverageByMap`;
    if (state[storeKey].has(mapName)) return;

    const urls = {
        detailed: `/api/detailed-coverage/${mapName}/coverage.json`,
        buildup:  `/api/buildup/${mapName}/coverage.json`,
        objects:  `/api/objects/${mapName}/coverage.json`
    };

    try {
        const res = await fetch(urls[type], { cache: "force-cache" });
        if (!res.ok) throw new Error(`coverage fetch failed: ${res.status}`);
        const json = await res.json();
        state[storeKey].set(mapName, json);
    } catch (err) {
        console.warn(`dcsMap: failed to load ${type} coverage for`, mapName, err);
        state[storeKey].set(mapName, {});
    }
};

window.dcsMap.isCovered = function(coverage, z, x, y) {
    const level = coverage[String(z)];
    if (!Array.isArray(level)) return false;
    return level.includes(`${x}:${y}`);
};

window.dcsMap.refreshAllSources = function() {
    const state = window.dcsMap._state;
    for (const source of [state.detailedSource, state.buildupSource, state.objectsSource]) {
        if (!source) continue;
        if (typeof source.clear === "function") source.clear();
        source.changed();
    }
};

// Keep old name working for any existing callers.
window.dcsMap.refreshDetailedSource = window.dcsMap.refreshAllSources;