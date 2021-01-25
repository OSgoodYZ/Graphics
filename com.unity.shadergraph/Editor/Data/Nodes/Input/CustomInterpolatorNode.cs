using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Internal;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEditor.Rendering;
using UnityEditor.ShaderGraph.Serialization;

namespace UnityEditor.ShaderGraph
{
    [Serializable]
    [Title("Custom Interpolators", "Instance")]
    class CustomInterpolatorNode : AbstractMaterialNode
    {
        [SerializeField]
        internal string customBlockNodeName = "K_INVALID";

        [SerializeField]
        private BlockNode.CustomBlockType serializedType = BlockNode.CustomBlockType.Vector4;

        // preview should be the CI value.
        public override bool hasPreview { get { return true; } }

        internal override bool ExposeToSearcher { get => false; } // This is exposed in a special way.

        internal BlockNode e_targetBlockNode // weak indirection via customBlockNodeName
        {
            get => (owner?.vertexContext.blocks.Find(cib => cib.value.descriptor.name == customBlockNodeName))?.value ?? null;
        }

        public CustomInterpolatorNode()
        {
            UpdateNodeAfterDeserialization();
        }

        internal void ConnectToCustomBlock(BlockNode node)
        {
            if (e_targetBlockNode != null)
            {
                e_targetBlockNode.UnregisterCallback(OnCustomBlockModified);
            }

            if (node?.isCustomBlock ?? false)
            {
                name = node.customName + " (Custom Interpolator)";
                customBlockNodeName = node.customName;
                serializedType = node.customWidth;
                BuildSlot();
                node.RegisterCallback(OnCustomBlockModified);
                // Maybe warn if they lack owners and weak-ref will be broken.
            }
        }

        internal void ConnectToCustomBlockByName(string customBlockName)
        {
            // target shouldn't really change, but if it did- we need to unregister.
            if (e_targetBlockNode != null)
            {
                e_targetBlockNode.UnregisterCallback(OnCustomBlockModified);
            }

            name = customBlockName + " (Custom Interpolator)";
            customBlockNodeName = customBlockName;
            if (e_targetBlockNode != null)
            {
                serializedType = e_targetBlockNode.customWidth;
                BuildSlot();                
                e_targetBlockNode.RegisterCallback(OnCustomBlockModified);
            }
            else // Target blockNode didn't actually exist :(.
            {
                // We should get badged in OnValidate.
            }
        }

        void OnCustomBlockModified(AbstractMaterialNode node, Graphing.ModificationScope scope)
        {
            if (node is BlockNode bnode)
            {
                if (bnode?.isCustomBlock ?? false)
                {
                    name = bnode.customName + " (Custom Interpolator)";
                    customBlockNodeName = bnode.customName;
                    if (e_targetBlockNode != null && e_targetBlockNode.owner != null)
                    {
                        serializedType = e_targetBlockNode.customWidth;
                        BuildSlot();
                        Dirty(ModificationScope.Node);
                        Dirty(ModificationScope.Topological);
                    }
                }
            }
            // bnode information we got is somehow invalid, this is probably case for an exception.
        }

        public override void ValidateNode()
        {
            // Our node was deleted or we had bad deserialization, we need to badge.
            if (e_targetBlockNode == null || e_targetBlockNode.owner == null)
            {
                e_targetBlockNode?.UnregisterCallback(OnCustomBlockModified);
                owner.AddValidationError(objectId, String.Format("Custom Block Interpolator '{0}' not found.", customBlockNodeName), ShaderCompilerMessageSeverity.Error);

            }
            else
            {
                // our blockNode reference is somehow valid again after it wasn't,
                // we can reconnect and everything should be restored.
                ConnectToCustomBlockByName(customBlockNodeName);
            }
        }

        public override void UpdateNodeAfterDeserialization()
        {
            // our e_targetBlockNode is unsafe here, so we build w/our serialization info and hope for the best!
            BuildSlot();
            base.UpdateNodeAfterDeserialization();
        }


        void BuildSlot()
        {
            switch (serializedType)
            {
                case BlockNode.CustomBlockType.Float:
                    AddSlot(new Vector1MaterialSlot(0, "Out", "Out", SlotType.Output, default(float), ShaderStageCapability.Fragment));
                    break;
                case BlockNode.CustomBlockType.Vector2:
                    AddSlot(new Vector2MaterialSlot(0, "Out", "Out", SlotType.Output, default(Vector2), ShaderStageCapability.Fragment));
                    break;
                case BlockNode.CustomBlockType.Vector3:
                    AddSlot(new Vector3MaterialSlot(0, "Out", "Out", SlotType.Output, default(Vector3), ShaderStageCapability.Fragment));
                    break;
                case BlockNode.CustomBlockType.Vector4:
                    AddSlot(new Vector4MaterialSlot(0, "Out", "Out", SlotType.Output, default(Vector4), ShaderStageCapability.Fragment));
                    break;
            }
            RemoveSlotsNameNotMatching(new[] { 0 });
        }


        public override string GetVariableNameForSlot(int slotid)
        {
            // Awkward case where current preview generation code does not use the Output for the all/isfinite preview for self.
            // GetOutputForSlot does _not_ make use of GetVariableNameForSlot in any way, so this is just to prevent disrupting
            // any existing expected behavior in the preview.
            return "float4(1,0,1,1)";
        }

        protected internal override string GetOutputForSlot(SlotReference fromSocketRef, ConcreteSlotValueType valueType, GenerationMode generationMode)
        {
            // check to see if we can inline a value.
            List<PreviewProperty> props = new List<PreviewProperty>();
            e_targetBlockNode?.CollectPreviewMaterialProperties(props);

            // if the cib is inActive, this node still might be in an active branch.
            bool isActive = e_targetBlockNode?.isActive ?? false;

            // if the cib has no input node, we can use the input property to inline a magic value.
            bool canInline = e_targetBlockNode?.GetInputNodeFromSlot(0) == null && props.Count != 0;

            // vector width of target slot
            int toWidth = CustomInterpolatorUtils.SlotTypeToWidth(valueType);

            string finalResult = "";

            // If cib is inactive (or doesn't exist), then we default to black (as is the case for other nodes).
            if (!isActive)
            {
                finalResult = CustomInterpolatorUtils.ConvertVector("$precision4(0,0,0,0)", 4, toWidth);
            }
            // cib has no input; we can directly use the inline value instead.
            else if (canInline)
            {
                Vector4 v = default;
                if (props[0].propType != PropertyType.Float)
                    v = props[0].vector4Value;

                int outWidth = 4;
                string result;
                switch (props[0].propType)
                {
                    case PropertyType.Float:
                        result = $" $precision1({props[0].floatValue}) ";
                        outWidth = 1;
                        break;
                    default:
                        result = $" $precision4({v.x},{v.y},{v.z},{v.w}) ";
                        outWidth = 4;
                        break;
                }
                finalResult = CustomInterpolatorUtils.ConvertVector(result, outWidth, toWidth);
            }
            // If we made it this far, then cib is in a valid and meaningful configuration in the SDI struct.
            else if (generationMode == GenerationMode.ForReals)
            {
                // pull directly out of the SDI and just use it.
                var result = string.Format("IN.{0}", customBlockNodeName);
                finalResult = CustomInterpolatorUtils.ConvertVector(result, (int)e_targetBlockNode.customWidth, toWidth);
            }
            // Preview doesn't support CI, but we can fake it by asking the cib's source input for it's value instead.
            else if (generationMode == GenerationMode.Preview)
            {
                var sourceSlot = FindSourceSlot(out var found);
                // CIB's type needs to constrain the incoming value (eg. vec2(out)->float(cib) | float(cin)->vec2(in))
                // If we didn't do this next line, we'd get vec2(out)->vec2(in), which would ignore the truncation in the preview.
                var result = sourceSlot.node.GetOutputForSlot(sourceSlot, FindSlot<MaterialSlot>(0).concreteValueType, GenerationMode.Preview);
                finalResult = CustomInterpolatorUtils.ConvertVector(result, (int)e_targetBlockNode.customWidth, toWidth);
            }
            return finalResult.Replace(PrecisionUtil.Token, concretePrecision.ToShaderString());
        }

        SlotReference FindSourceSlot(out bool found)
        {
            try
            {
                found = true;
                return owner.GetEdges(e_targetBlockNode).First().outputSlot;
            }
            catch
            {
                found = false;
                return default;
            }
        }
    }
}
