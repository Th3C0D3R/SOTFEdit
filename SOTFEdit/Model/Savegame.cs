﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using SOTFEdit.Infrastructure;
using static SOTFEdit.Model.Constants.Actors;

namespace SOTFEdit.Model;

public class Savegame : ObservableObject
{
    public Savegame(string fullPath, string dirName, SavegameStore savegameStore)
    {
        SavegameStore = savegameStore;
        FullPath = fullPath;
        Title = dirName;
    }

    public string FullPath { get; }

    public SavegameStore SavegameStore { get; }

    public string Title { get; }

    public DateTime LastSaveTime => ReadLastSaveTime();
    public BitmapImage Thumbnail => BuildThumbnail();

    public string PrintableType
    {
        get
        {
            if (IsSinglePlayer())
            {
                return "SP";
            }

            if (IsMultiPlayer())
            {
                return "MP";
            }

            if (IsMultiPlayerClient())
            {
                return "MP_Client";
            }

            return "Unknown";
        }
    }

    private BitmapImage BuildThumbnail()
    {
        var thumbPath = SavegameStore.GetThumbPath() ??
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "default_screenshot.png");

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(thumbPath);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.EndInit();
        return image;
    }

    public long RegrowTrees(bool createBackup, VegetationState vegetationStateSelected)
    {
        if (SavegameStore.LoadJsonRaw(SavegameStore.FileType.WorldObjectLocatorManagerSaveData) is not JObject
            objectLocatorSaveData)
        {
            return 0;
        }

        var countRegrown = 0;

        var worldObjectLocatorManagerToken = objectLocatorSaveData.SelectToken("Data.WorldObjectLocatorManager");
        if (worldObjectLocatorManagerToken?.ToString() is not { } worldObjectLocatorManagerJson ||
            JsonConverter.DeserializeRaw(worldObjectLocatorManagerJson) is not JObject worldObjectLocatorManager)
        {
            return 0;
        }

        var serializedStates = worldObjectLocatorManager["SerializedStates"]?.ToList() ?? Enumerable.Empty<JToken>();

        foreach (var serializedState in serializedStates)
        {
            var valueToken = serializedState["Value"];
            var value = valueToken?.Value<short>();
            if (value == null)
            {
                continue;
            }

            var shiftedValue = (short)(1 << (value - 1));

            if (!Enum.IsDefined(typeof(VegetationState), shiftedValue) ||
                !vegetationStateSelected.HasFlag((VegetationState)shiftedValue))
            {
                continue;
            }

            serializedState.Remove();
            countRegrown++;
        }

        if (countRegrown == 0)
        {
            return 0;
        }

        worldObjectLocatorManagerToken.Replace(JsonConverter.Serialize(worldObjectLocatorManager));

        SavegameStore.StoreJson(SavegameStore.FileType.WorldObjectLocatorManagerSaveData, objectLocatorSaveData,
            createBackup);

        return countRegrown;
    }

    public bool ReviveFollower(int typeId, HashSet<int> itemIds, Outfit? outfit, Position pos, bool createBackup)
    {
        var reviveResult = ReviveFollowerInSaveData(typeId, itemIds, outfit, pos, createBackup);
        var gameStateKey = typeId == KelvinTypeId ? "IsRobbyDead" : "IsVirginiaDead";

        var modifyGameStateResult = ModifyGameState(new Dictionary<string, object>
        {
            { gameStateKey, false }
        }, createBackup);

        return reviveResult || modifyGameStateResult;
    }

    private bool ReviveFollowerInSaveData(int typeId, HashSet<int> itemIds, Outfit? outfit, Position pos, bool createBackup)
    {
        if (SavegameStore.LoadJsonRaw(SavegameStore.FileType.SaveData) is not JObject saveData)
        {
            return false;
        }

        var saveDataWrapper = new SaveDataWrapper(saveData);
        var followerModifier = new FollowerModifier(saveDataWrapper);
        var hasChanges = followerModifier.Revive(typeId, itemIds, outfit, pos) && saveDataWrapper.SerializeAllModified();

        if (!hasChanges)
        {
            return false;
        }

        SavegameStore.StoreJson(SavegameStore.FileType.SaveData, saveData, createBackup);

        return true;
    }


    private DateTime ReadLastSaveTime()
    {
        if (SavegameStore.LoadJsonRaw(SavegameStore.FileType.GameStateSaveData) is not JObject gameStateData)
        {
            return SavegameStore.LastWriteTime;
        }

        var gameStateToken = gameStateData.SelectToken("Data.GameState");
        if (gameStateToken?.ToString() is not { } gameStateString ||
            JsonConverter.DeserializeRaw(gameStateString) is not JObject gameState)
        {
            return SavegameStore.LastWriteTime;
        }

        return gameState["SaveTime"]?.ToObject<DateTime>() ?? SavegameStore.LastWriteTime;
    }

    public bool ModifyGameState(Dictionary<string, object> values, bool createBackup)
    {
        if (SavegameStore.LoadJsonRaw(SavegameStore.FileType.GameStateSaveData) is not JObject gameStateData)
        {
            return false;
        }

        var gameStateToken = gameStateData.SelectToken("Data.GameState");
        if (gameStateToken?.ToString() is not { } gameStateString ||
            JsonConverter.DeserializeRaw(gameStateString) is not JObject gameState)
        {
            return false;
        }

        var hasChanges = false;

        foreach (var keyValuePair in values)
        {
            var token = gameState[keyValuePair.Key];
            var valueAsToken = JToken.FromObject(keyValuePair.Value);
            if (token == null || token.Equals(valueAsToken))
            {
                continue;
            }

            token.Replace(valueAsToken);
            hasChanges = true;
        }

        if (!hasChanges)
        {
            return false;
        }

        gameStateToken.Replace(JsonConverter.Serialize(gameState));
        SavegameStore.StoreJson(SavegameStore.FileType.GameStateSaveData, gameStateData, createBackup);

        return true;
    }

    public bool IsSinglePlayer()
    {
        return ParentDirIs("singleplayer");
    }

    public bool IsMultiPlayer()
    {
        return ParentDirIs("multiplayer");
    }

    public bool IsMultiPlayerClient()
    {
        return ParentDirIs("multiplayerclient");
    }

    public bool HasUnknownParentDir()
    {
        return !IsSinglePlayer() && !IsMultiPlayer() && !IsMultiPlayerClient();
    }

    private bool ParentDirIs(string value)
    {
        return SavegameStore.GetParentDirectory()?.Name.ToLower().Equals(value) ?? false;
    }
}