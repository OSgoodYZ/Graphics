using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;
using Pool = UnityEngine.Pool;

namespace UnityEditor.ShaderGraph
{
    internal static class CustomInterpolatorUtils
    {
        internal static bool generatorSkipFlag = false;
        internal static bool generatorNodeOnly = false;

        internal static IEnumerable<CustomInterpolatorNode> GetCIBDependents(BlockNode bnode)
        {
            return bnode?.owner?.GetNodes<CustomInterpolatorNode>().Where(cin => cin.e_targetBlockNode == bnode).ToList()
                ?? new List<CustomInterpolatorNode>();
        }
    }
 
    internal class CISubGen
    {
        #region descriptor
        internal static readonly string k_splicePreInclude = "sgci_PreInclude";
        internal static readonly string k_splicePrePacking = "sgci_PrePacking";
        internal static readonly string k_splicePreSurface = "sgci_PreSurface";
        internal static readonly string k_splicePreVertex = "sgci_PreVertex";
        internal static readonly string k_spliceCopyToSDI = "sgci_CopyToSDI";

        [GenerationAPI]
        internal struct Descriptor
        {
            internal string src, dst; // for function or block.
            internal string name;     // for struct or function.
            internal string define;   // defined for client code to indicate we're live.
            internal string splice;   // splice location, prefer use something from the list.

            internal bool isBlock => src != null && dst != null && name == null && splice != null;
            internal bool isStruct => src == null && dst == null && name != null && splice != null;
            internal bool isFunc => src != null && dst != null && name != null && splice != null;
            internal bool isDefine => define != null && splice != null && src == null && dst == null & name == null;

            internal static Descriptor MakeFunc(string splice, string name, string dstType, string srcType, string define = "") => new Descriptor { splice = splice, name = name, dst = dstType, src = srcType, define = define };
            internal static Descriptor MakeStruct(string splice, string name, string define = "") => new Descriptor { splice = splice, name = name, define = define };
            internal static Descriptor MakeBlock(string splice, string dst, string src) => new Descriptor { splice = splice, dst = dst, src = src };
            internal static Descriptor MakeDefine(string splice, string define) => new Descriptor { splice = splice, define = define };
        }

        [GenerationAPI]
        internal class Collection : IEnumerable<Collection.Item>
        {
            public class Item
            {
                public Descriptor descriptor { get; }
                public Item(Descriptor descriptor) { this.descriptor = descriptor; }
            }
            readonly List<Collection.Item> m_Items;
            public Collection() { m_Items = new List<Collection.Item>(); }
            public Collection Add(Collection structs) { foreach (Collection.Item item in structs) m_Items.Add(item); return this; }
            public Collection Add(Descriptor descriptor) { m_Items.Add(new Collection.Item(descriptor)); return this; }
            public IEnumerator<Item> GetEnumerator() { return m_Items.GetEnumerator(); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }
        #endregion

        private List<BlockNode> customBlockNodes;
        private bool isNodePreview;
        private Dictionary<string, ShaderStringBuilder> spliceCommandBuffer;

        internal CISubGen(bool isNodePreview)
        {
            this.isNodePreview = isNodePreview;
            customBlockNodes = new List<BlockNode>();
            spliceCommandBuffer = new Dictionary<String, ShaderStringBuilder>();
        }

        #region GeneratorEntryPoints
        internal void ProcessExistingStackData(List<AbstractMaterialNode> vertexNodes, List<MaterialSlot> vertexSlots, List<AbstractMaterialNode> pixelNodes, IActiveFieldsSet activeFields)
        {
            if (CustomInterpolatorUtils.generatorSkipFlag)
                return;

            // departing from current generation code, we will select what to generate based on some graph analysis.
            foreach (var cin in pixelNodes.OfType<CustomInterpolatorNode>().ToList())
            {
                // The CustomBlockNode's subtree.
                var anties = GetAntecedents(cin.e_targetBlockNode)?.Where(a => !vertexNodes.Contains(a) && !pixelNodes.Contains(a));

                // cin contains an inlined value, so there is nothing to do.
                if (anties == null)
                {
                    continue;
                }
                else if (isNodePreview)
                {
                    // we can cheat and add the sub-tree to the pixel node list, which ciNode digs into during its own preview generation.
                    pixelNodes.InsertRange(0, anties);
                    // pixelNodes.AddRange(anties.Where(a => !pixelNodes.Contains(a)));
                }
                else // it's a full compile and cin isn't inlined, so do all the things.
                {
                    activeFields.AddAll(cin.e_targetBlockNode.descriptor); // Make New FieldDescriptor for VertexDescription
                    customBlockNodes.Add(cin.e_targetBlockNode);
                    vertexNodes.AddRange(anties);
                    vertexNodes.Add(cin.e_targetBlockNode);
                    vertexSlots.Add(cin.e_targetBlockNode.FindSlot<MaterialSlot>(0));
                }
            }
        }

        internal List<StructDescriptor> CopyModifyExistingPassStructs(IEnumerable<StructDescriptor> passStructs, IActiveFieldsSet activeFields)
        {
            if (CustomInterpolatorUtils.generatorSkipFlag)
                return passStructs.ToList();

            var newPassStructs = new List<StructDescriptor>();

            foreach (var ps in passStructs)
            {
                if (ps.populateWithCustomInterpolators)
                {
                    var agg = new List<FieldDescriptor>();
                    foreach (var cib in customBlockNodes)
                    {
                        var fd = new FieldDescriptor(ps.name, cib.customName, "", ShaderValueTypeFrom((int)cib.customWidth), subscriptOptions: StructFieldOptions.Generated);

                        agg.Add(fd);
                        activeFields.AddAll(fd);
                    }
                    newPassStructs.Add(new StructDescriptor { name = ps.name, packFields = ps.packFields, fields = ps.fields.Union(agg).ToArray() });
                }
                else
                {
                    newPassStructs.Add(ps);
                }
            }

            foreach (var cid in customBlockNodes.Select(bn => bn.descriptor))
                activeFields.AddAll(cid);

            return newPassStructs;
        }

        internal void ProcessDescriptors(IEnumerable<Descriptor> descriptors)
        {
            if (CustomInterpolatorUtils.generatorSkipFlag)
                return;

            ShaderStringBuilder builder = new ShaderStringBuilder();
            foreach (var desc in descriptors)
            {
                builder.Clear();
                if      (desc.isBlock)  GenCopyBlock(desc.dst, desc.src, builder);
                else if (desc.isFunc)   GenCopyFunc(desc.name, desc.dst, desc.src, builder, desc.define);
                else if (desc.isStruct) GenStruct(desc.name, builder, desc.define);
                else if (desc.isDefine) builder.AppendLine($"#define {desc.define}");
                else continue;

                if (!spliceCommandBuffer.ContainsKey(desc.splice))
                    spliceCommandBuffer.Add(desc.splice, new ShaderStringBuilder());

                spliceCommandBuffer[desc.splice].Concat(builder);
            }
        }

        internal void AppendToSpliceCommands(Dictionary<string, string> spliceCommands)
        {
            if (CustomInterpolatorUtils.generatorSkipFlag)
                return;

            foreach (var spliceKV in spliceCommandBuffer)
                spliceCommands.Add(spliceKV.Key, spliceKV.Value.ToCodeBlock());
        }
    #endregion

    #region helpers
    private void GenStruct(string structName, ShaderStringBuilder builder, string makeDefine = "")
        {
            builder.AppendLine($"struct {structName}");
            builder.AppendLine("{");
            using (builder.IndentScope())
            {
                foreach (var bn in customBlockNodes)
                {

                    builder.AppendLine($"float{bn.customWidth} {bn.customName};");
                }
            }
            builder.AppendLine("};");
            if (makeDefine != null && makeDefine != "")
                builder.AppendLine($"#define {makeDefine}");

            builder.AppendNewLine();
        }

        private void GenCopyBlock(string dst, string src, ShaderStringBuilder builder)
        {
            foreach (var bnode in customBlockNodes)
                builder.AppendLine($"{dst}.{bnode.customName} = {src}.{bnode.customName};");
        }

        private void GenCopyFunc(string funcName, string dstType, string srcType, ShaderStringBuilder builder, string makeDefine = "")
        {
            builder.AppendLine($"{dstType} {funcName}(inout {dstType} output, {srcType} input)");
            using (builder.BlockScope())
            {
                GenCopyBlock("output", "input", builder);
                builder.AppendLine("return output;");
            }
            if (makeDefine != null && makeDefine != "")
                builder.AppendLine($"#define {makeDefine}");
        }

        private static List<AbstractMaterialNode> GetAntecedents(BlockNode blockNode)
        {
            if (blockNode != null && blockNode.isCustomBlock && blockNode.isActive && blockNode.GetInputNodeFromSlot(0) != null)
            {
                List<AbstractMaterialNode> results = new List<AbstractMaterialNode>();
                NodeUtils.DepthFirstCollectNodesFromNode(results, blockNode, NodeUtils.IncludeSelf.Exclude);
                return results != null && results.Count() == 0 ? null : results;
            }
            return null;
        }

        private static ShaderValueType ShaderValueTypeFrom(int width)
        {
            switch (width)
            {
                case 1: return ShaderValueType.Float;
                case 2: return ShaderValueType.Float2;
                case 3: return ShaderValueType.Float3;
                default: return ShaderValueType.Float4;
            }
        }
        #endregion
    }
}
