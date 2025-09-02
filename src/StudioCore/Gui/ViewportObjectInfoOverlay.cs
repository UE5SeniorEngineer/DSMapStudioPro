using static Andre.Native.ImGuiBindings;
using StudioCore.MsbEditor;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Veldrid.Utilities;

namespace StudioCore.Gui;

/// <summary>
/// Handles the viewport overlay for displaying object information (lights, chests, drops, etc.)
/// when they appear in the viewport
/// </summary>
public class ViewportObjectInfoOverlay
{
    private readonly Universe _universe;
    private readonly Selection _selection;
    
    // Cache for performance
    private List<ObjectInfo> _visibleObjects = new();
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(100); // Update 10 times per second
    
    public ViewportObjectInfoOverlay(Universe universe, Selection selection)
    {
        _universe = universe;
        _selection = selection;
    }
    
    /// <summary>
    /// Information about an object to display in the overlay
    /// </summary>
    public class ObjectInfo
    {
        public Entity Entity { get; set; }
        public Vector3 WorldPosition { get; set; }
        public Vector2 ScreenPosition { get; set; }
        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string ObjectID { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
    }
    
    /// <summary>
    /// Updates the list of visible objects that should have info panels
    /// </summary>
    public void UpdateVisibleObjects(BoundingFrustum frustum, WorldView worldView, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, int viewportWidth, int viewportHeight)
    {
        if (DateTime.Now - _lastUpdate < _updateInterval)
            return;
            
        _lastUpdate = DateTime.Now;
        _visibleObjects.Clear();
        
        if (!CFG.Current.Viewport_ShowObjectInfo)
            return;
            
        var cameraPosition = worldView.CameraTransform.Position;
        var maxDistance = CFG.Current.Viewport_ObjectInfo_MaxDistance;
        
        foreach (var (mapName, map) in _universe.LoadedObjectContainers)
        {
            if (map?.Objects == null)
                continue;
                
            foreach (var entity in map.Objects)
            {
                if (!IsTargetObjectType(entity))
                    continue;
                    
                var worldPos = entity.GetRootLocalTransform().Position;
                var distance = Vector3.Distance(cameraPosition, worldPos);
                
                // Skip if too far away
                if (distance > maxDistance)
                    continue;
                    
                // Check if in frustum
                var boundingBox = new BoundingBox(worldPos - Vector3.One * 0.5f, worldPos + Vector3.One * 0.5f);
                var containment = frustum.Contains(boundingBox);
                if (containment == ContainmentType.Disjoint)
                    continue;
                    
                // Convert world position to screen position
                var screenPos = WorldToScreen(worldPos, viewMatrix, projectionMatrix, viewportWidth, viewportHeight);
                if (screenPos.HasValue)
                {
                    var objInfo = CreateObjectInfo(entity, worldPos, screenPos.Value);
                    if (objInfo != null)
                        _visibleObjects.Add(objInfo);
                }
            }
        }
    }
    
    /// <summary>
    /// Renders the object information panels
    /// </summary>
    public unsafe void RenderOverlay()
    {
        if (!CFG.Current.Viewport_ShowObjectInfo || _visibleObjects.Count == 0)
            return;

        // Render each object info as a separate window
        foreach (var objInfo in _visibleObjects)
        {
            RenderObjectInfoWindow(objInfo);
        }
    }
    
    /// <summary>
    /// Determines if an entity is a target object type (light, chest, drop, etc.)
    /// </summary>
    private bool IsTargetObjectType(Entity entity)
    {
        if (entity?.WrappedObject == null)
            return false;
            
        var wrappedType = entity.WrappedObject.GetType();
        var typeName = wrappedType.Name;
        
        // Check for lights
        if (CFG.Current.Viewport_ShowObjectInfo_Lights)
        {
            if (IsLightObject(entity, typeName))
                return true;
        }
        
        // Check for chests/treasures
        if (CFG.Current.Viewport_ShowObjectInfo_Chests)
        {
            if (IsChestObject(entity, typeName))
                return true;
        }
        
        // Check for drops
        if (CFG.Current.Viewport_ShowObjectInfo_Drops)
        {
            if (IsDropObject(entity, typeName))
                return true;
        }
        
        // Check for NPCs/Enemies
        if (CFG.Current.Viewport_ShowObjectInfo_NPCs)
        {
            if (IsNPCObject(entity, typeName))
                return true;
        }
        
        // Check for special objects
        if (CFG.Current.Viewport_ShowObjectInfo_Special)
        {
            if (IsSpecialObject(entity, typeName))
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if an entity represents a light source
    /// </summary>
    private bool IsLightObject(Entity entity, string typeName)
    {
        var modelName = GetModelName(entity);
        var entityName = entity.Name?.ToLower() ?? "";
        
        // Check model name patterns
        if (ContainsAnyKeyword(modelName, new[] { "light", "lamp", "torch", "candle", "fire", "flame", "brazier", "lantern", "sconce" }))
            return true;
            
        // Check entity name patterns
        if (ContainsAnyKeyword(entityName, new[] { "light", "torch", "candle", "fire", "flame", "brazier", "lantern" }))
            return true;
            
        // Check for specific MSB types that are typically lights
        if (entity.WrappedObject is MSBE.Part.Asset assetE)
        {
            // Check EntityID patterns that typically indicate lights (game-specific)
            var entityId = assetE.EntityID;
            // Elden Ring light entity IDs often start with specific ranges
            if (entityId >= 1001000 && entityId <= 1002000) // Example range for environmental lights
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if an entity represents a treasure chest
    /// </summary>
    private bool IsChestObject(Entity entity, string typeName)
    {
        var modelName = GetModelName(entity);
        var entityName = entity.Name?.ToLower() ?? "";
        
        // Check model name patterns
        if (ContainsAnyKeyword(modelName, new[] { "chest", "treasure", "coffer", "box", "crate", "container" }))
            return true;
            
        // Check entity name patterns
        if (ContainsAnyKeyword(entityName, new[] { "chest", "treasure", "coffer", "box" }))
            return true;
            
        // Check for treasure-specific entity ID patterns
        if (entity.WrappedObject is MSBE.Part.Asset assetE)
        {
            var entityId = assetE.EntityID;
            // Treasure chests often have specific entity ID ranges
            if (entityId >= 1100000 && entityId <= 1200000) // Example treasure range
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if an entity represents a dropped item
    /// </summary>
    private bool IsDropObject(Entity entity, string typeName)
    {
        var modelName = GetModelName(entity);
        var entityName = entity.Name?.ToLower() ?? "";
        
        // Check model name patterns
        if (ContainsAnyKeyword(modelName, new[] { "drop", "item", "pickup", "loot", "corpse" }))
            return true;
            
        // Check entity name patterns  
        if (ContainsAnyKeyword(entityName, new[] { "drop", "item", "pickup", "loot", "corpse" }))
            return true;
            
        // Check for item-specific entity ID patterns
        if (entity.WrappedObject is MSBE.Part.Asset assetE)
        {
            var entityId = assetE.EntityID;
            // Dropped items often have specific entity ID ranges
            if (entityId >= 1200000 && entityId <= 1300000) // Example item drop range
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if an entity represents an NPC or enemy
    /// </summary>
    private bool IsNPCObject(Entity entity, string typeName)
    {
        var entityName = entity.Name?.ToLower() ?? "";
        
        // Check for enemy/NPC parts
        if (entity.WrappedObject is MSBE.Part.Enemy ||
            entity.WrappedObject is MSB3.Part.Enemy ||
            entity.WrappedObject is MSB1.Part.Enemy ||
            entity.WrappedObject is MSBS.Part.Enemy ||
            entity.WrappedObject is MSBB.Part.Enemy ||
            entity.WrappedObject is MSBD.Part.Enemy)
            return true;
            
        // Check entity name patterns
        if (ContainsAnyKeyword(entityName, new[] { "npc", "enemy", "merchant", "blacksmith", "vendor" }))
            return true;
            
        return false;
    }
    
    /// <summary>
    /// Checks if an entity represents a special interactive object
    /// </summary>
    private bool IsSpecialObject(Entity entity, string typeName)
    {
        var modelName = GetModelName(entity);
        var entityName = entity.Name?.ToLower() ?? "";
        
        // Check model name patterns for interactive objects
        if (ContainsAnyKeyword(modelName, new[] { "door", "lever", "switch", "button", "altar", "statue", 
                                                  "crystal", "monument", "pillar", "elevator", "lift",
                                                  "bridge", "gate", "portal", "teleport", "bonfire",
                                                  "grace", "checkpoint", "fog" }))
            return true;
            
        // Check entity name patterns
        if (ContainsAnyKeyword(entityName, new[] { "door", "lever", "switch", "altar", "crystal", 
                                                   "elevator", "bonfire", "grace", "fog", "gate" }))
            return true;
            
        return false;
    }
    
    /// <summary>
    /// Gets the model name from various MSB part types
    /// </summary>
    private string GetModelName(Entity entity)
    {
        return entity.WrappedObject switch
        {
            MSBE.Part.Asset assetE => assetE.ModelName?.ToLower() ?? "",
            MSB3.Part.Object obj3 => obj3.ModelName?.ToLower() ?? "",
            MSB1.Part.Object obj1 => obj1.ModelName?.ToLower() ?? "",
            MSBS.Part.Object objS => objS.ModelName?.ToLower() ?? "",
            MSBB.Part.Object objB => objB.ModelName?.ToLower() ?? "",
            MSB2.Part.Object obj2 => obj2.ModelName?.ToLower() ?? "",
            MSBD.Part.Object objD => objD.ModelName?.ToLower() ?? "",
            _ => ""
        };
    }
    
    /// <summary>
    /// Checks if a string contains any of the specified keywords
    /// </summary>
    private bool ContainsAnyKeyword(string text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text))
            return false;
            
        return keywords.Any(keyword => text.Contains(keyword));
    }
    
    /// <summary>
    /// Creates object information for display
    /// </summary>
    private ObjectInfo CreateObjectInfo(Entity entity, Vector3 worldPos, Vector2 screenPos)
    {
        var objInfo = new ObjectInfo
        {
            Entity = entity,
            WorldPosition = worldPos,
            ScreenPosition = screenPos,
            ObjectName = entity.Name ?? "Unnamed",
            ObjectID = GetEntityID(entity)
        };
        
        // Determine object type
        if (IsLightObject(entity, entity.WrappedObject.GetType().Name))
            objInfo.ObjectType = "Light";
        else if (IsChestObject(entity, entity.WrappedObject.GetType().Name))
            objInfo.ObjectType = "Chest";
        else if (IsDropObject(entity, entity.WrappedObject.GetType().Name))
            objInfo.ObjectType = "Drop";
        else if (IsNPCObject(entity, entity.WrappedObject.GetType().Name))
            objInfo.ObjectType = "NPC";
        else if (IsSpecialObject(entity, entity.WrappedObject.GetType().Name))
            objInfo.ObjectType = "Special";
        else
            objInfo.ObjectType = "Object";
            
        // Add additional properties
        objInfo.Properties["Position"] = $"({worldPos.X:F1}, {worldPos.Y:F1}, {worldPos.Z:F1})";
        objInfo.Properties["Model"] = GetModelName(entity);
        objInfo.Properties["Type"] = GetPartTypeName(entity);
        
        // Add entity ID if available
        var entityId = GetEntityIDValue(entity);
        if (entityId.HasValue)
            objInfo.Properties["EntityID"] = entityId.Value.ToString();
        
        // Add rotation if available
        var transform = entity.GetRootLocalTransform();
        if (transform.Rotation != Quaternion.Identity)
            objInfo.Properties["Rotation"] = $"({transform.EulerRotation.X:F1}, {transform.EulerRotation.Y:F1}, {transform.EulerRotation.Z:F1})";
        
        return objInfo;
    }
    
    /// <summary>
    /// Gets a display-friendly entity ID
    /// </summary>
    private string GetEntityID(Entity entity)
    {
        var entityId = GetEntityIDValue(entity);
        if (entityId.HasValue)
            return entityId.Value.ToString();
            
        return entity.WrappedObject?.GetHashCode().ToString() ?? "Unknown";
    }
    
    /// <summary>
    /// Gets the actual entity ID value from various MSB part types
    /// </summary>
    private uint? GetEntityIDValue(Entity entity)
    {
        return entity.WrappedObject switch
        {
            MSBE.Part.Asset assetE => (uint?)assetE.EntityID,
            MSB3.Part.Object obj3 => (uint?)obj3.EntityID,
            MSBS.Part.Object objS => (uint?)objS.EntityID,
            MSBB.Part.Object objB => (uint?)objB.EntityID,
            MSB1.Part.Object obj1 => (uint?)obj1.EntityID,
            MSBD.Part.Object objD => (uint?)objD.EntityID,
            _ => null
        };
    }
    
    /// <summary>
    /// Gets a friendly name for the part type
    /// </summary>
    private string GetPartTypeName(Entity entity)
    {
        return entity.WrappedObject switch
        {
            MSBE.Part.Asset => "Asset",
            MSB3.Part.Object => "Object",
            MSBS.Part.Object => "Object",
            MSBB.Part.Object => "Object",
            MSB1.Part.Object => "Object",
            MSB2.Part.Object => "Object",
            MSBD.Part.Object => "Object",
            MSBE.Part.Enemy => "Enemy",
            MSB3.Part.Enemy => "Enemy",
            MSBS.Part.Enemy => "Enemy",
            MSBB.Part.Enemy => "Enemy",
            MSB1.Part.Enemy => "Enemy",
            MSBD.Part.Enemy => "Enemy",
            _ => entity.WrappedObject?.GetType().Name ?? "Unknown"
        };
    }
    
    /// <summary>
    /// Renders a single object information window
    /// </summary>
    private unsafe void RenderObjectInfoWindow(ObjectInfo objInfo)
    {
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | 
                    ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;

        var windowId = $"ObjectInfo_{objInfo.ObjectID}";
        
        // Adjust window size based on content
        var windowSize = new Vector2(220, 100);
        if (objInfo.Properties.ContainsKey("EntityID"))
            windowSize.Y += 15;
        if (objInfo.Properties.ContainsKey("Rotation"))
            windowSize.Y += 15;
        
        ImGui.SetNextWindowPos(objInfo.ScreenPosition);
        ImGui.SetNextWindowSize(windowSize);
        ImGui.SetNextWindowBgAlpha(CFG.Current.Viewport_ObjectInfo_PanelOpacity);
        
        if (ImGui.Begin(windowId, flags))
        {
            // Title with object type and name
            ImGui.TextColored(GetTypeColor(objInfo.ObjectType), $"{objInfo.ObjectType}: {objInfo.ObjectName}");
            
            // Properties
            if (objInfo.Properties.ContainsKey("Type"))
                ImGui.Text($"Type: {objInfo.Properties["Type"]}");
                
            if (objInfo.Properties.ContainsKey("EntityID"))
                ImGui.Text($"ID: {objInfo.Properties["EntityID"]}");
            else
                ImGui.Text($"Hash: {objInfo.ObjectID}");
            
            if (objInfo.Properties.ContainsKey("Position"))
                ImGui.Text($"Pos: {objInfo.Properties["Position"]}");
                
            if (objInfo.Properties.ContainsKey("Rotation"))
                ImGui.Text($"Rot: {objInfo.Properties["Rotation"]}");
                
            if (objInfo.Properties.ContainsKey("Model") && !string.IsNullOrEmpty(objInfo.Properties["Model"].ToString()))
            {
                var modelName = objInfo.Properties["Model"].ToString();
                if (modelName != "none" && modelName != "")
                    ImGui.Text($"Model: {modelName}");
            }
        }
        ImGui.End();
    }
    
    /// <summary>
    /// Gets a color for the object type
    /// </summary>
    private Vector4 GetTypeColor(string objectType)
    {
        return objectType switch
        {
            "Light" => new Vector4(1.0f, 1.0f, 0.6f, 1.0f),   // Light yellow
            "Chest" => new Vector4(1.0f, 0.8f, 0.4f, 1.0f),   // Orange/gold
            "Drop" => new Vector4(0.6f, 1.0f, 0.6f, 1.0f),    // Light green
            "NPC" => new Vector4(1.0f, 0.6f, 0.6f, 1.0f),     // Light red
            "Special" => new Vector4(0.8f, 0.6f, 1.0f, 1.0f), // Light purple
            _ => new Vector4(0.8f, 0.8f, 0.8f, 1.0f)          // Light gray
        };
    }
    
    /// <summary>
    /// Converts world position to screen coordinates
    /// </summary>
    private Vector2? WorldToScreen(Vector3 worldPos, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, int viewportWidth, int viewportHeight)
    {
        // Transform to view space
        var viewPos = Vector3.Transform(worldPos, viewMatrix);
        
        // Transform to clip space
        var clipPos = Vector4.Transform(new Vector4(viewPos, 1.0f), projectionMatrix);
        
        // Check if behind camera
        if (clipPos.W <= 0)
            return null;
            
        // Perspective divide
        var ndcPos = new Vector3(clipPos.X / clipPos.W, clipPos.Y / clipPos.W, clipPos.Z / clipPos.W);
        
        // Check if outside NDC bounds
        if (Math.Abs(ndcPos.X) > 1.0f || Math.Abs(ndcPos.Y) > 1.0f)
            return null;
            
        // Convert to screen coordinates
        var screenX = (ndcPos.X + 1.0f) * 0.5f * viewportWidth;
        var screenY = (1.0f - ndcPos.Y) * 0.5f * viewportHeight;
        
        return new Vector2(screenX, screenY);
    }
}