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
        
        return false;
    }
    
    /// <summary>
    /// Checks if an entity represents a light source
    /// </summary>
    private bool IsLightObject(Entity entity, string typeName)
    {
        // Light objects are typically Asset parts with specific model names
        if (entity.WrappedObject is MSBE.Part.Asset assetE)
        {
            var modelName = assetE.ModelName?.ToLower() ?? "";
            return modelName.Contains("light") || modelName.Contains("lamp") || 
                   modelName.Contains("torch") || modelName.Contains("candle") ||
                   modelName.Contains("fire") || modelName.Contains("flame");
        }
        
        if (entity.WrappedObject is MSB3.Part.Object obj3)
        {
            var modelName = obj3.ModelName?.ToLower() ?? "";
            return modelName.Contains("light") || modelName.Contains("lamp") || 
                   modelName.Contains("torch") || modelName.Contains("candle") ||
                   modelName.Contains("fire") || modelName.Contains("flame");
        }
        
        if (entity.WrappedObject is MSB1.Part.Object obj1)
        {
            var modelName = obj1.ModelName?.ToLower() ?? "";
            return modelName.Contains("light") || modelName.Contains("lamp") || 
                   modelName.Contains("torch") || modelName.Contains("candle") ||
                   modelName.Contains("fire") || modelName.Contains("flame");
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if an entity represents a treasure chest
    /// </summary>
    private bool IsChestObject(Entity entity, string typeName)
    {
        if (entity.WrappedObject is MSBE.Part.Asset assetE)
        {
            var modelName = assetE.ModelName?.ToLower() ?? "";
            return modelName.Contains("chest") || modelName.Contains("treasure") ||
                   modelName.Contains("coffer") || modelName.Contains("box");
        }
        
        if (entity.WrappedObject is MSB3.Part.Object obj3)
        {
            var modelName = obj3.ModelName?.ToLower() ?? "";
            return modelName.Contains("chest") || modelName.Contains("treasure") ||
                   modelName.Contains("coffer") || modelName.Contains("box");
        }
        
        if (entity.WrappedObject is MSB1.Part.Object obj1)
        {
            var modelName = obj1.ModelName?.ToLower() ?? "";
            return modelName.Contains("chest") || modelName.Contains("treasure") ||
                   modelName.Contains("coffer") || modelName.Contains("box");
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if an entity represents a dropped item
    /// </summary>
    private bool IsDropObject(Entity entity, string typeName)
    {
        if (entity.WrappedObject is MSBE.Part.Asset assetE)
        {
            var modelName = assetE.ModelName?.ToLower() ?? "";
            return modelName.Contains("drop") || modelName.Contains("item") ||
                   modelName.Contains("pickup") || modelName.Contains("loot");
        }
        
        if (entity.WrappedObject is MSB3.Part.Object obj3)
        {
            var modelName = obj3.ModelName?.ToLower() ?? "";
            return modelName.Contains("drop") || modelName.Contains("item") ||
                   modelName.Contains("pickup") || modelName.Contains("loot");
        }
        
        if (entity.WrappedObject is MSB1.Part.Object obj1)
        {
            var modelName = obj1.ModelName?.ToLower() ?? "";
            return modelName.Contains("drop") || modelName.Contains("item") ||
                   modelName.Contains("pickup") || modelName.Contains("loot");
        }
        
        return false;
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
            ObjectID = entity.WrappedObject?.GetHashCode().ToString() ?? "Unknown"
        };
        
        // Determine object type
        if (IsLightObject(entity, entity.WrappedObject.GetType().Name))
            objInfo.ObjectType = "Light";
        else if (IsChestObject(entity, entity.WrappedObject.GetType().Name))
            objInfo.ObjectType = "Chest";
        else if (IsDropObject(entity, entity.WrappedObject.GetType().Name))
            objInfo.ObjectType = "Drop";
        else
            objInfo.ObjectType = "Object";
            
        // Add additional properties
        objInfo.Properties["Position"] = $"({worldPos.X:F1}, {worldPos.Y:F1}, {worldPos.Z:F1})";
        
        if (entity.WrappedObject is MSBE.Part.Asset assetE)
        {
            objInfo.Properties["Model"] = assetE.ModelName ?? "None";
            objInfo.Properties["Type"] = "Asset";
        }
        else if (entity.WrappedObject is MSB3.Part.Object obj3)
        {
            objInfo.Properties["Model"] = obj3.ModelName ?? "None";
            objInfo.Properties["Type"] = "Object";
        }
        else if (entity.WrappedObject is MSB1.Part.Object obj1)
        {
            objInfo.Properties["Model"] = obj1.ModelName ?? "None";
            objInfo.Properties["Type"] = "Object";
        }
        
        return objInfo;
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
        
        ImGui.SetNextWindowPos(objInfo.ScreenPosition);
        ImGui.SetNextWindowSize(new Vector2(200, 80));
        ImGui.SetNextWindowBgAlpha(CFG.Current.Viewport_ObjectInfo_PanelOpacity);
        
        if (ImGui.Begin(windowId, flags))
        {
            ImGui.Text($"{objInfo.ObjectType}: {objInfo.ObjectName}");
            ImGui.Text($"ID: {objInfo.ObjectID}");
            
            if (objInfo.Properties.ContainsKey("Position"))
                ImGui.Text(objInfo.Properties["Position"].ToString());
                
            if (objInfo.Properties.ContainsKey("Model"))
                ImGui.Text($"Model: {objInfo.Properties["Model"]}");
        }
        ImGui.End();
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