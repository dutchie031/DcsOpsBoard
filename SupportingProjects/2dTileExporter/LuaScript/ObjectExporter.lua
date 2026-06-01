
---@class ObjectExportSettings
---@field public exportPath string
---@field public exportSummaryPath string
---@field public topLeft Vec2
---@field public bottomRight Vec2
---@field public exportPointWhenNoBox boolean

local Terrain         		= require('terrain')


local terrainAsJson = net.lua2json(Terrain)
env.info("ObjectExporter: ".. terrainAsJson)

local lfs = lfs
local io = io
local math = math
local string = string

local function getExportSettings()
    if env.mission.theatre == "Caucasus" then
        return {
            exportPath = "C:\\DCS_Exports\\DcsOpsBoard\\Object_Export\\Caucasus\\objects.json",
            exportSummaryPath = "C:\\DCS_Exports\\DcsOpsBoard\\Object_Export\\Caucasus\\objects.summary.json",
            topLeft     = { x = 65000,   y = 0      },
            bottomRight = { x = -450000, y = 950000  },
            exportPointWhenNoBox = false,
        }
    end
end

-- ─── helpers ────────────────────────────────────────────────────────────────

local function ensureDir(path)
    local cur = ""
    for part in string.gmatch(path, "[^\\/]+") do
        cur = cur .. part .. "\\"
        if not lfs.attributes(cur, "mode") then
            lfs.mkdir(cur)
        end
    end
end

--- Rotate a 2-D local-space offset (lx forward, lz right) by the object's
--- orientation vectors and add the world-space origin.
--- Returns { x, z } in DCS world space (x = East-ish, z = North-ish).
local function toWorldXZ(origin, fwd, right, lx, lz)
    return {
        x = origin.x + fwd.x * lx + right.x * lz,
        z = origin.z + fwd.z * lx + right.z * lz,
    }
end

--- Build the four footprint corners for one object.
--- Returns an array of 4 { x, z } world-space points (closed ring).
local function buildFootprint(pos, bbox)
    local o   = pos.p                   -- world-space origin
    local fwd = pos.x                   -- forward unit vector
    local rgt = pos.z                   -- right unit vector

    local x0, x1 = bbox.min.x, bbox.max.x
    local z0, z1 = bbox.min.z, bbox.max.z

    return {
        toWorldXZ(o, fwd, rgt, x0, z0),
        toWorldXZ(o, fwd, rgt, x1, z0),
        toWorldXZ(o, fwd, rgt, x1, z1),
        toWorldXZ(o, fwd, rgt, x0, z1),
    }
end

local function buildFootprintFromTerrainObb(terrainDesc, pos)
    if not terrainDesc or not terrainDesc.sizeOBB or not pos or not pos.p or not pos.x or not pos.z then
        return nil
    end

    local sizeX = terrainDesc.sizeOBB[1]
    local sizeZ = terrainDesc.sizeOBB[2]
    local rotation = 0

    if not sizeX or not sizeZ then
        return nil
    end

    local hx = sizeX * 0.5
    local hz = sizeZ * 0.5
    local cosR = math.cos(rotation)
    local sinR = math.sin(rotation)

    -- Use object transform as the world anchor.
    -- Terrain descriptor rotation is intentionally ignored because it can be in a
    -- different frame and cause double-rotation for instanced scenery.
    local function rotateLocal(dx, dz)
        local lx = (dx * cosR - dz * sinR)
        local lz = (dx * sinR + dz * cosR)
        return toWorldXZ(pos.p, pos.x, pos.z, lx, lz)
    end

    return {
        rotateLocal(-hx, -hz),
        rotateLocal( hx, -hz),
        rotateLocal( hx,  hz),
        rotateLocal(-hx,  hz),
    }
end

local function buildFootprintFromTerrainAabb(terrainDesc, pos)
    if not terrainDesc or not terrainDesc.boxMin or not terrainDesc.boxMax or not pos or not pos.p or not pos.x or not pos.z then
        return nil
    end

    local minX = terrainDesc.boxMin[1]
    local minZ = terrainDesc.boxMin[2]
    local maxX = terrainDesc.boxMax[1]
    local maxZ = terrainDesc.boxMax[2]

    if not minX or not minZ or not maxX or not maxZ then
        return nil
    end

    -- Re-center the box so any absolute/model-frame offsets don't leak into world coords.
    local hx = math.abs(maxX - minX) * 0.5
    local hz = math.abs(maxZ - minZ) * 0.5

    return {
        toWorldXZ(pos.p, pos.x, pos.z, -hx, -hz),
        toWorldXZ(pos.p, pos.x, pos.z,  hx, -hz),
        toWorldXZ(pos.p, pos.x, pos.z,  hx,  hz),
        toWorldXZ(pos.p, pos.x, pos.z, -hx,  hz),
    }
end

--- Heading in degrees (0 = North, clockwise) derived from the forward vector.
local function headingDeg(fwd)
    local deg = math.deg(math.atan2(fwd.x, fwd.z))
    if deg < 0 then deg = deg + 360 end
    return deg
end

-- ─── cache type descriptors so we only call getDescByName once per type ─────

local descCache = {}
local function getTypeDesc(typeName)
    if descCache[typeName] == nil then
        descCache[typeName] = SceneryObject.getDescByName(typeName) or false
    end
    return descCache[typeName] or nil
end

local function normalizeName(value)
    if not value then return "" end
    local lower = string.lower(value)
    return (lower:gsub("[^a-z0-9]", ""))
end

local function getTerrainDescForType(typeName, position)
    if not Terrain.getObjectsAtMapPoint then
        return nil
    end

    local terrainObjects = Terrain.getObjectsAtMapPoint(position.p.x, position.p.z)
    if not terrainObjects or #terrainObjects == 0 then
        return nil
    end

    local wanted = normalizeName(typeName)
    local first = terrainObjects[1]

    for _, terrainObj in ipairs(terrainObjects) do
        if terrainObj and terrainObj.model then
            local model = normalizeName(terrainObj.model)
            if model ~= "" and (model == wanted or model:find(wanted, 1, true) or wanted:find(model, 1, true)) then
                return terrainObj
            end
        end
    end

    if #terrainObjects > 1 then
        env.info("ObjectExporter: WARNING - no terrain model match for type '" .. tostring(typeName) .. "' among " .. tostring(#terrainObjects) .. " candidates, using first")
    end

    if first and not first.model then
        env.info("ObjectExporter: WARNING - no model name found on first terrain object for type '" .. tostring(typeName) .. "', using first")
    end

    return first
end

-- ─── collection ─────────────────────────────────────────────────────────────

local exportSettings = getExportSettings()
if not exportSettings then
    env.info("ObjectExporter: ERROR - no export settings for theatre: " .. tostring(env.mission.theatre))
    return
end

local features = {}
local exported = 0
local exported_without_box = 0
local exported_exact_box = 0
local exported_terrain_box = 0
local exported_points_only = 0
local skipped_without_geometry = 0

local missedByType = {}

local terrainDescSamplePerType = {}

---@param object Object
local function onObjectFound(object, _)
    if not object then return end

    exported = exported + 1
    if exported % 500 == 0 then
        env.info("ObjectExporter: collected " .. exported .. " objects...")
    end

    local desc = object:getDesc()
    if not (desc and desc.typeName) then return end

    local typeName = desc.typeName
    local typeDesc = getTypeDesc(typeName)

  

    -- Always get position (gives orientation as well as point)
    local pos = object:getPosition()   -- Position3: { p, x, y, z }

    local feature = {
        typeName  = typeName,
        category  = desc.category,
        x         = pos.p.x,
        z         = pos.p.z,
        altitude  = pos.p.y,
        heading   = headingDeg(pos.x),
    }

    local bbox = nil
    local bboxSource = nil
    local footprint = nil
    local height = nil

    if desc.box then
        bbox = desc.box
        bboxSource = "desc"
    elseif typeDesc and typeDesc.box then
        bbox = typeDesc.box
        bboxSource = "type"
    else
        local terrainDesc = getTerrainDescForType(typeName, pos)
        if terrainDescSamplePerType[typeName] == nil then
            terrainDescSamplePerType[typeName] = terrainDesc or false
        end

        if terrainDesc then
            footprint = buildFootprintFromTerrainObb(terrainDesc, pos)
            if footprint then
                bboxSource = "terrainObb"
            else
                footprint = buildFootprintFromTerrainAabb(terrainDesc, pos)
                if footprint then
                    bboxSource = "terrainAabb"
                end
            end

            if terrainDesc.radius then
                height = terrainDesc.radius * 2
            end
        end
    end

    if bbox or footprint then
        if bbox then
            feature.height = bbox.max.y - bbox.min.y
            feature.footprint = buildFootprint(pos, bbox)
            exported_exact_box = exported_exact_box + 1
        else
            feature.footprint = footprint
            if height then
                feature.height = height
            end
            exported_terrain_box = exported_terrain_box + 1
        end

        feature.bboxSource = bboxSource
    else
        exported_without_box = exported_without_box + 1

        local missed = missedByType[typeName]
        if not missed then

            missed = {
                count = 0,
                attributes = desc.attributes,
                desc = desc,
            }
            missedByType[typeName] = missed
        end
        missed.count = missed.count + 1

        if exportSettings.exportPointWhenNoBox then
            exported_points_only = exported_points_only + 1
        else
            skipped_without_geometry = skipped_without_geometry + 1
            return
        end
    end

    features[#features + 1] = feature
end

-- ─── run search ─────────────────────────────────────────────────────────────

local searchBox = {
    id = world.VolumeType.BOX,
    params = {
        min = { x = exportSettings.bottomRight.x, y = 0,    z = exportSettings.topLeft.y     },
        max = { x = exportSettings.topLeft.x,     y = 1000, z = exportSettings.bottomRight.y },
    }
}

world.searchObjects(Object.Category.SCENERY, searchBox, onObjectFound)
env.info("ObjectExporter: search complete, " .. exported .. " objects found, building JSON...")

-- ─── serialise ──────────────────────────────────────────────────────────────

--- Minimal JSON serialiser (avoids net.lua2json on large tables for speed).
local function jsonVal(v)
    local t = type(v)
    if t == "nil"     then return "null"
    elseif t == "boolean" then return tostring(v)
    elseif t == "number"  then
        -- Avoid scientific notation for positions; keep 6 decimal places
        return string.format("%.6f", v)
    elseif t == "string"  then
        -- Escape special characters
        local s = v:gsub('\\', '\\\\'):gsub('"', '\\"'):gsub('\n', '\\n'):gsub('\r', '\\r')
        return '"' .. s .. '"'
    end
    return "null"
end

local function serializeXZ(pt)
    return '{"x":' .. jsonVal(pt.x) .. ',"z":' .. jsonVal(pt.z) .. '}'
end

local function serializeFootprint(fp)
    local parts = {}
    for _, pt in ipairs(fp) do
        parts[#parts + 1] = serializeXZ(pt)
    end
    return '[' .. table.concat(parts, ',') .. ']'
end

local function serializeFeature(f)
    local buf = {}
    buf[#buf+1] = '"typeName":'  .. jsonVal(f.typeName)
    buf[#buf+1] = '"category":'  .. jsonVal(f.category)
    buf[#buf+1] = '"x":'         .. jsonVal(f.x)
    buf[#buf+1] = '"z":'         .. jsonVal(f.z)
    buf[#buf+1] = '"altitude":'  .. jsonVal(f.altitude)
    buf[#buf+1] = '"heading":'   .. jsonVal(f.heading)
    if f.height    then buf[#buf+1] = '"height":'    .. jsonVal(f.height)            end
    if f.footprint then buf[#buf+1] = '"footprint":' .. serializeFootprint(f.footprint) end
    return '{' .. table.concat(buf, ',') .. '}'
end

-- ─── write file ─────────────────────────────────────────────────────────────

local exportPath = exportSettings.exportPath
-- ensure output directory exists
local dir = exportPath:match("^(.*[\\/])")
if dir then ensureDir(dir) end

local file = io.open(exportPath, "w+")
if not file then
    env.info("ObjectExporter: ERROR - could not open output file: " .. exportPath)
    return
end

file:write('{"objects":[\n')
for i, f in ipairs(features) do
    local line = serializeFeature(f)
    if i < #features then
        file:write(line .. ',\n')
    else
        file:write(line .. '\n')
    end
end
file:write(']}')
file:close()

env.info("ObjectExporter: written " .. #features .. " features to " .. exportPath)
env.info("ObjectExporter: " .. exported_without_box .. " objects had no bounding box info")
env.info("ObjectExporter: " .. exported_exact_box .. " with exact box, " .. exported_terrain_box .. " with terrain-derived box, " .. exported_points_only .. " points only")
env.info("ObjectExporter: " .. skipped_without_geometry .. " skipped without geometry")

-- Optional summary JSON with diagnostics to tune your export filters.
local function topByCount(input, limit)
    local rows = {}
    for typeName, value in pairs(input) do
        rows[#rows + 1] = {
            typeName = typeName,
            count = value.count,
            attributes = value.attributes,
            desc = value.desc,
        }
    end
    table.sort(rows, function(a, b) return a.count > b.count end)
    local n = math.min(limit, #rows)
    local out = {}
    for i = 1, n do
        out[#out + 1] = rows[i]
    end
    return out
end

local function serializeKeyCountRows(rows)
    local parts = {}
    for _, row in ipairs(rows) do
        local attributesJson = "null"
        if row.attributes then
            attributesJson = net.lua2json(row.attributes)
        end

        local descJson = "null"
        if row.desc then
            descJson = net.lua2json(row.desc)
        end

        parts[#parts + 1] = '{"typeName":' .. jsonVal(row.typeName) .. ',"attributes":' .. attributesJson .. ',"count":' .. jsonVal(row.count) .. ',"desc":' .. descJson ..  '}'
    end
    return '[' .. table.concat(parts, ',') .. ']'
end

local summaryPath = exportSettings.exportSummaryPath
local summaryDir = summaryPath:match("^(.*[\\/])")
if summaryDir then ensureDir(summaryDir) end

local summaryFile = io.open(summaryPath, "w+")
if summaryFile then
    local topMissing = topByCount(missedByType, 100)

    summaryFile:write('{')
    summaryFile:write('"exportedTotal":' .. jsonVal(exported) .. ',')
    summaryFile:write('"exportedFeatures":' .. jsonVal(#features) .. ',')
    summaryFile:write('"withoutBox":' .. jsonVal(exported_without_box) .. ',')
    summaryFile:write('"exactBox":' .. jsonVal(exported_exact_box) .. ',')
    summaryFile:write('"terrainBox":' .. jsonVal(exported_terrain_box) .. ',')
    summaryFile:write('"pointsOnly":' .. jsonVal(exported_points_only) .. ',')
    summaryFile:write('"skippedWithoutGeometry":' .. jsonVal(skipped_without_geometry) .. ',')
    summaryFile:write('"top100Missed":' .. serializeKeyCountRows(topMissing))
    summaryFile:write('}')
    summaryFile:close()
    env.info("ObjectExporter: wrote summary to " .. summaryPath)
else
    env.info("ObjectExporter: WARNING - could not write summary file: " .. summaryPath)
end

-- ─── export terrainDescPerType ───────────────────────────────────────────────

local terrainPath = exportSettings.exportPath:gsub("objects%.json$", "terrain_desc_per_type.json")
local terrainDir = terrainPath:match("^(.*[\\/])")
if terrainDir then ensureDir(terrainDir) end

local terrainFile = io.open(terrainPath, "w+")
if terrainFile then
    local rows = {}
    for typeName, value in pairs(terrainDescSamplePerType) do
        local valueJson = "null"
        if value ~= false then
            valueJson = net.lua2json(value)
        end
        rows[#rows + 1] = '{"typeName":' .. jsonVal(typeName) .. ',"terrainDesc":' .. (valueJson or "null") .. '}'
    end
    terrainFile:write('{"terrainDescPerType":[\n')
    for i, row in ipairs(rows) do
        if i < #rows then
            terrainFile:write(row .. ',\n')
        else
            terrainFile:write(row .. '\n')
        end
    end
    terrainFile:write(']}')
    terrainFile:close()
    env.info("ObjectExporter: wrote terrain desc to " .. terrainPath)
else
    env.info("ObjectExporter: WARNING - could not write terrain desc file: " .. terrainPath)
end

