

--[[
    This script exports terrain surface data 
]]


local lfs = lfs
local io = io
local math = math
local string = string

---@class ExportSettings
---@field sampleInterval number Interval in meters between samples
---@field heightMapInterval number
---@field topLeft Vec2
---@field bottomRight Vec2
---@field outputFolder string
---@field groundHeightMap { maxHeight: number, surfaceType: SurfaceType }[]

local startAtTile = -1 -- for resuming exports

local tileSize = 1024  -- number of intervals per tile side
local function getExportSettings()
    if(env.mission.theatre == "Caucasus") then
        return {
            sampleInterval = 5,
            heightMapInterval = 5,
            topLeft = {x = 65000, y = 0},
            bottomRight = {x = -450000, y = 950000},
            outputFolder = "C:\\DCS_Exports\\DcsOpsBoard\\Caucasus",
            groundHeightMap = {
                { maxHeight = 1000, surfaceType = 6 },
                { maxHeight = 2500, surfaceType = 7 },
                { maxHeight = 3200, surfaceType = 8},
                { maxHeight = 9000, surfaceType = 10}
            }
        }
    end

    if(env.mission.theatre == "Kola") then
        return {
            sampleInterval = 5,
            heightMapInterval = 5,
            topLeft = {x = 584915, y = -671814},
            bottomRight = {x = -314667, y = 855667},
            outputFolder = "C:\\DCS_Exports\\DcsOpsBoard\\Kola",
            groundHeightMap = {
                { maxHeight = 400, surfaceType = 6 },
                { maxHeight = 900, surfaceType = 7 },
                { maxHeight = 1500, surfaceType = 8},
                { maxHeight = 9000, surfaceType = 10}
            }
        }
    end
end


env.info("LuaExport: Loading LuaExport.lua")

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

---@class Logger
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


---@class Exporter
---@field logger Logger
local Exporter = {}
Exporter.__index = Exporter

---@return Exporter
function Exporter.New()
    local self = setmetatable({}, Exporter)
    return self
end

---@param settings ExportSettings
---@param logger Logger
function Exporter:Export(settings, logger)
    ensureDir(settings.outputFolder)
    self.logger = logger
    self.logger:Log("Starting export...")

    local function WriteMetaDataJson()
        local hmStep = settings.heightMapInterval / settings.sampleInterval  -- e.g. 4 at 40m/10m
        local meta = {
            topLeft = settings.topLeft,
            bottomRight = settings.bottomRight,
            sampleInterval = settings.sampleInterval,
            tileSamples = tileSize + 1,  -- number of samples per tile side
            tileSize = tileSize,
            heightMapInterval = settings.heightMapInterval,
            heightMapSamples  = math.floor(tileSize / hmStep) + 1
        }

        local json  = net.lua2json(meta)

        local path = settings.outputFolder .. "\\metadata.json"
        local file = io.open(path, "w+")
        if not file then
            self.logger:Log("Failed to open metadata file for writing: " .. path)
            return
        end
        file:write(json)
        file:close()
    end

    WriteMetaDataJson()

    local function WriteTileMetadataJson(tileIndex, tileX, tileY)

        local meta = {
            tileIndex = tileIndex,
            tileOrigin = {x = tileX, y = tileY},
            sampleInterval = settings.sampleInterval,
            tileSamples = tileSize + 1,  -- number of samples per tile side
            tileSize = tileSize
        }

        local string  = net.lua2json(meta)

        local path = settings.outputFolder .. "\\tile_" .. tostring(tileIndex) .. "_metadata.json"
        local file = io.open(path, "w+")
        if not file then
            self.logger:Log("Failed to open tile metadata file for writing: " .. path)
            return
        end
        file:write(string)
        file:close()

    end

    -- 1 byte: surface type (0-255)
    local function EncodeTerrainType(surfaceType)
        return string.char(surfaceType % 256)
    end

    -- 4 bytes: height as little-endian int32 (handles negative sub-sea values)
    local function EncodeHeight(height)
        local h = math.floor(height)
        if h < 0 then h = h + 4294967296 end
        return string.char(
            h                          % 256,
            math.floor(h / 256)        % 256,
            math.floor(h / 65536)      % 256,
            math.floor(h / 16777216)   % 256
        )
    end

    -- assert(loadfile("C:\\Repos\\DCS\\DcsMissionPlanner\\DcsMissionPlanner\\LuaExportScript\\LuaExport.lua"))()

    local startX = settings.topLeft.x
    local endX = settings.bottomRight.x
    local startY = settings.topLeft.y
    local endY = settings.bottomRight.y

    local stepX, stepY = -settings.sampleInterval, settings.sampleInterval  -- stepX negative (down), stepY positive (right)
    local tileStepX, tileStepY = -tileSize * settings.sampleInterval, tileSize * settings.sampleInterval  -- tileStepX negative, tileStepY positive

    self.logger:Log(string.format("Exporting terrain data from (%.2f, %.2f) to (%.2f, %.2f) with sample interval %.2f m", startX, startY, endX, endY, settings.sampleInterval))

    local tileIndex = 0;
    -- Fixed loops: X from top to bottom (decreasing), Y from left to right (increasing)
    for x = startX, endX, tileStepX do  
        for y = startY, endY, tileStepY do 
            if tileIndex > startAtTile then
                WriteTileMetadataJson(tileIndex, x, y)
        
                local startTime = os.time()
            
                self.logger:Log(string.format("Processing tile at (%d, %d)", x, y))
                local TerrainDataPath = settings.outputFolder .. "\\tile_" .. tostring(tileIndex) .. ".td.bin"
                local terrainDataFile = io.open(TerrainDataPath, "wb")
                if not terrainDataFile then
                    self.logger:Log("Failed to open file for writing: " .. TerrainDataPath)
                    return
                end

                local HeightMapDataPath = settings.outputFolder .. "\\tile_" .. tostring(tileIndex) .. ".hm.bin"
                local heightMapFile = io.open(HeightMapDataPath, "wb")
                if not heightMapFile then
                    terrainDataFile:close()
                    self.logger:Log("Failed to open file for writing: " .. HeightMapDataPath)
                    return
                end
                

                pcall(function() if heightMapFile.setvbuf then heightMapFile:setvbuf("full") end end)
                pcall(function() if terrainDataFile.setvbuf then terrainDataFile:setvbuf("full") end end)

                local hmStep = settings.heightMapInterval / settings.sampleInterval  -- e.g. 4 at 40m/10m

                local terrainBuf = {""}
                local heightBuf  = {""}
                local sampleCountX = 0
                for sampleX = x, x + tileStepX, stepX do
                    local sampleCountY = 0
                    for sampleY = y, y + tileStepY, stepY do
                        local vec = {x = sampleX, y = sampleY}
                        local surfaceType, height = getDataAt(vec)

                        if (surfaceType == 1) then
                            -- ground, determine by height
                            for _, mapping in ipairs(settings.groundHeightMap) do
                                if height <= mapping.maxHeight then
                                    surfaceType = mapping.surfaceType
                                    break
                                end
                            end
                        end
                            
                        terrainBuf[#terrainBuf + 1] = EncodeTerrainType(surfaceType)
                        
                         -- height: only every hmStep samples in both axes
                        if sampleCountX % hmStep == 0 and sampleCountY % hmStep == 0 then
                            heightBuf[#heightBuf + 1] = EncodeHeight(height)
                        end

                        sampleCountY = sampleCountY + 1
                    end
                    sampleCountX = sampleCountX + 1
                end
                terrainDataFile:write(table.concat(terrainBuf))
                terrainDataFile:close()
                heightMapFile:write(table.concat(heightBuf))
                heightMapFile:close()


               self.logger:Log(string.format(
                    "Finished tile %d in %d seconds", tileIndex, os.difftime(os.time(), startTime)
                ))
            end
            tileIndex = tileIndex + 1
        end
    end
end

local function calculateTotalTiles(settings)
    local width = math.abs(settings.topLeft.x - settings.bottomRight.x)
    local height = math.abs(settings.topLeft.y - settings.bottomRight.y)
    local tilesX = math.ceil(width / (tileSize * settings.sampleInterval))
    local tilesY = math.ceil(height / (tileSize * settings.sampleInterval))
    return tilesX * tilesY
end



local exporter = Exporter.New()
local settings = getExportSettings()
local logger = Logger.New(settings.outputFolder .. "\\export_log.txt")

if settings then
    local totalTiles = calculateTotalTiles(settings)
    logger:Log(string.format("Total tiles to export: %d", totalTiles))
    exporter:Export(settings, logger)
end

logger:Close()

env.info("LuaExport: Finished LuaExport.lua")




