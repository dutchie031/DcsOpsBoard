
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
                if shape.polygonMode == "rect" then
                    local centerX = shape.mapX
                    local centerY = shape.mapY
                    local halfWidth = shape.width / 2
                    local halfHeight = shape.height / 2

                    local box = {
                        TopLeft = { x = centerX - halfWidth, y = centerY - halfHeight },
                        BottomRight = { x = centerX + halfWidth, y = centerY + halfHeight }
                    }

                    table.insert(boxes, box)
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

    for index, box in ipairs(boxes) do
        self.logger:Log(string.format("Exporting box %d: TopLeft(%.2f, %.2f), BottomRight(%.2f, %.2f)", index, box.TopLeft.x, box.TopLeft.y, box.BottomRight.x, box.BottomRight.y))
        writeMetadataTable(index, box, settings)

        local terrainBuf = {""}
        for x = box.TopLeft.x, box.BottomRight.x, settings.sampleInterval do
            for y = box.TopLeft.y, box.BottomRight.y, settings.sampleInterval do
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

        local TerrainDataPath = settings.outputFolder .. "\\tile_" .. tostring(index) .. ".td.bin"
        local terrainDataFile = io.open(TerrainDataPath, "wb")
        if not terrainDataFile then
            self.logger:Log("Failed to open file for writing: " .. TerrainDataPath)
            return
        end

        terrainDataFile:write(table.concat(terrainBuf))
        terrainDataFile:close()

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