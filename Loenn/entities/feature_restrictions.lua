local drawableRectangle = require("structs.drawable_rectangle")
local utils = require("utils")

local restrictions = {}
restrictions.name = "Akron/featureRestrictions"
restrictions.depth = -1000000
restrictions.placements = {
    {
        name = "map_features",
        data = {
            features = "Freeze,FrameAdvance,Timescale"
        }
    }
}
restrictions.fieldInformation = {
    features = {
        fieldType = "string"
    }
}

function restrictions.sprite(room, entity)
    return drawableRectangle.fromRectangle("bordered", entity.x - 8, entity.y - 8, 16, 16,
        {0.15, 0.35, 0.55, 0.6}, {0.4, 0.8, 1.0, 1.0})
end

function restrictions.selection(room, entity)
    return utils.rectangle(entity.x - 8, entity.y - 8, 16, 16)
end

return restrictions
