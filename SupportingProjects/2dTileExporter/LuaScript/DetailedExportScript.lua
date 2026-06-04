
--[[
    This Script exports all tile data with a specified sample interval in all the rectangle trigger zones.

]]--

local lfs = lfs
local io = io
local math = math
local string = string

---@class DetailedExportSettings
---@field sampleInterval number
---@field outputFolder string
---@field groundHeightMap { maxHeight: number, surfaceType: SurfaceType }[]

local function getExportSettings()

    if(env.mission.theatre == "Caucasus") then

        return {
            sampleInterval = 1,
            outputFolder = "C:\\DCS_Exports\\DcsOpsBoard\\Detailed_Export\\Caucasus",
            groundHeightMap = {
                { maxHeight = 1000, surfaceType = 6 },
                { maxHeight = 2500, surfaceType = 7 },
                { maxHeight = 3200, surfaceType = 8},
                { maxHeight = 9000, surfaceType = 10}
            }
        }
    end
end

env.info("Starting detailed export script")

---@param vec Vec2
---@return SurfaceType, number
local function getDataAt(vec)
    local surfaceType = land.getSurfaceType(vec)
    local height = land.getHeight(vec)
    return surfaceType, height
end

local function ensureDir(path)
    if not lfs.attributes(path, "mode") then
        -- try create recursively
        local cur = ""
        for part in string.gmatch(path, "[^\\/]+") do
            cur = cur .. part .. "\\"
            if not lfs.attributes(cur, "mode") then
                lfs.mkdir(cur)
            end
        end
    end
    env.info("Directory ensured: " .. path)
end


---@class DetailedLogger
---@field file file*?
local Logger = {}
Logger.__index = Logger

function Logger.New(filePath)
    local self = setmetatable({}, Logger)
    self.file = io.open(filePath, "w+")
    return self
end

function Logger:Log(line)
    if self.file then
        self.file:write(line .. "\n")
    end
end

function Logger:Close()
    if self.file then
        self.file:close()
        self.file = nil
    end
end


---@class DetailedExporter
---@field logger DetailedLogger
local Exporter = {}
Exporter.__index = Exporter

---@param logger DetailedLogger
---@return DetailedExporter
function Exporter.New(logger)
    local self = setmetatable({}, Exporter)
    self.logger = logger
    return self
end


---@class ExportBox
---@field TopLeft Vec2
---@field BottomRight Vec2
function getExportBoxes()
    ---@type Array<ExportBox>
    local boxes = {}
    if env.mission.drawings and env.mission.drawings.layers then
        for _, layer in pairs(env.mission.drawings.layers) do
            for _, shape in pairs(layer.objects) do
                -- Only handle free polygons
                if shape.polygonMode == "free" and shape.points then
                    -- Collect numeric keys and sort them to preserve order
                    local keys = {}
                    for k, _ in pairs(shape.points) do
                        if type(k) == "number" then
                            table.insert(keys, k)
                        end
                    end
                    table.sort(keys)

                    local verts = {}
                    local minX, minY, maxX, maxY
                    for _, k in ipairs(keys) do
                        local p = shape.points[k]
                        if p and p.x and p.y then
                            local ax = shape.mapX + p.x
                            local ay = shape.mapY + p.y
                            table.insert(verts, { x = ax, y = ay })
                            if not minX or ax < minX then minX = ax end
                            if not maxX or ax > maxX then maxX = ax end
                            if not minY or ay < minY then minY = ay end
                            if not maxY or ay > maxY then maxY = ay end
                        end
                    end

                    if #verts >= 3 then
                        local box = {
                            Vertices = verts,
                            TopLeft = { x = minX, y = minY },
                            BottomRight = { x = maxX, y = maxY }
                        }
                        table.insert(boxes, box)
                    else
                        env.info("Skipping polygon with fewer than 3 vertices")
                    end
                end
            end
        end
    end

    return boxes
end

---@param tileIndex number
---@param box ExportBox
---@param settings DetailedExportSettings
function writeMetadataTable(tileIndex, box, settings)
    local meta = {
        topLeft = box.TopLeft,
        bottomRight = box.BottomRight,
        sampleInterval = settings.sampleInterval
    }

    if box.Vertices then
        meta.vertices = box.Vertices
    end

    local json  = net.lua2json(meta)

    local fileName = "metadata_" .. tileIndex .. ".json"
    local path = settings.outputFolder .. "\\" .. fileName
    local file = io.open(path, "w+")
    if not file then
        env.error("Failed to open metadata file for writing: " .. path)
        return
    end
    file:write(json)
    file:close()
end

---@param settings DetailedExportSettings
function Exporter:Export(settings)

    self.logger:Log("Export started with settings: " .. net.lua2json(settings))

    local boxes = getExportBoxes()

    local function EncodeTerrainType(surfaceType)
        return string.char(surfaceType % 256)
    end

        -- Ray-casting point-in-polygon (odd-even rule)
        local function pointInPolygon(px, py, vertices)
            local inside = false
            local j = #vertices
            for i = 1, #vertices do
                local xi = vertices[i].x
                local yi = vertices[i].y
                local xj = vertices[j].x
                local yj = vertices[j].y
                local intersect = ((yi > py) ~= (yj > py)) and (px < (xj - xi) * (py - yi) / (yj - yi + 0.0) + xi)
                if intersect then
                    inside = not inside
                end
                j = i
            end
            return inside
        end

    for index, box in ipairs(boxes) do
        self.logger:Log(string.format("Exporting box %d: TopLeft(%.2f, %.2f), BottomRight(%.2f, %.2f)", index, box.TopLeft.x, box.TopLeft.y, box.BottomRight.x, box.BottomRight.y))
        writeMetadataTable(index, box, settings)

        local terrainBuf = {""}
        local outsideCount = 0
        for x = box.TopLeft.x, box.BottomRight.x, settings.sampleInterval do
            for y = box.TopLeft.y, box.BottomRight.y, settings.sampleInterval do
                local isInside = true
                if box.Vertices then
                    isInside = pointInPolygon(x, y, box.Vertices)
                end

                if not isInside then
                    -- write sentinel 255 for outside samples
                    terrainBuf[#terrainBuf+1] = string.char(255)
                    outsideCount = outsideCount + 1
                else
                    local surfaceType, height = getDataAt({ x = x, y = y })
                    if (surfaceType == 1) then
                        -- ground, determine by height
                        for _, mapping in ipairs(settings.groundHeightMap) do
                            if height <= mapping.maxHeight then
                                surfaceType = mapping.surfaceType
                                break
                            end
                        end
                    end
                    terrainBuf[#terrainBuf+1] = EncodeTerrainType(surfaceType)
                end
            end
        end

        local TerrainDataPath = settings.outputFolder .. "\\tile_" .. tostring(index) .. ".td.bin"
        local terrainDataFile = io.open(TerrainDataPath, "wb")
        if not terrainDataFile then
            self.logger:Log("Failed to open file for writing: " .. TerrainDataPath)
            return
        end

        terrainDataFile:write(table.concat(terrainBuf))
        terrainDataFile:close()

        self.logger:Log(string.format("Tile %d wrote %d outside samples (sentinel 255)", index, outsideCount))

    end
    
    self.logger:Log("Export completed for " .. #boxes .. " boxes.")
end

local settings = getExportSettings()
if not settings then
    env.error("No export settings found for this theatre.")
else
    ensureDir(settings.outputFolder)
    local logger = Logger.New(settings.outputFolder .. "\\export_log.txt")
    local exporter = Exporter.New(logger)
    exporter:Export(settings)
    logger:Close()
end