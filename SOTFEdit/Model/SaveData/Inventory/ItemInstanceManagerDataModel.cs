﻿using System.Collections.Generic;

namespace SOTFEdit.Model.SaveData.Inventory;

public record ItemInstanceManagerDataModel : SotfBaseModel
{
    public List<ItemBlockModel> ItemBlocks { get; set; }
}