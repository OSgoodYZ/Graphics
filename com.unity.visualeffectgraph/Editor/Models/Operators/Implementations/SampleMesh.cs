using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor.VFX.Operator
{
    class SampleMeshProvider : VariantProvider
    {
        protected override sealed Dictionary<string, object[]> variants
        {
            get
            {
                return new Dictionary<string, object[]>
                {
                    { "source", Enum.GetValues(typeof(SampleMesh.SourceType)).Cast<object>().ToArray() },
                };
            }
        }
    }

    [VFXInfo(category = "Sampling", variantProvider = typeof(SampleMeshProvider), experimental = true)]
    class SampleMesh : VFXOperator
    {
        override public string name
        {
            get
            {
                if (source == SourceType.Mesh)
                    return "Sample Mesh";
                else
                    return "Sample Skinned Mesh";
            }
        }

        public enum PlacementMode
        {
            Vertex,
            Edge,
            Surface
        };

        public class InputPropertiesMesh
        {
            [Tooltip("Sets the Mesh to sample from.")]
            public Mesh mesh = VFXResources.defaultResources.mesh;
        }

        public class InputPropertiesSkinnedMeshRenderer
        {
            [Tooltip("Sets the Mesh to sample from, has to be an exposed entry.")]
            public SkinnedMeshRenderer skinnedMesh = null;
        }

        public class InputPropertiesPlacementVertex
        {
            [Tooltip("Sets the vertex index to read from.")]
            public uint vertex = 0u;
        }

        public enum SurfaceCoordinates
        {
            Barycentric,
            Uniform,
        }

        public class InputPropertiesPlacementSurfaceBarycentricCoordinates
        {
            [Tooltip("The triangle index to read from.")]
            public uint triangle = 0u;

            [Tooltip("Barycentric coordinate (z is computed from x & y).")]
            public Vector2 barycentric;
        }

        public class InputPropertiesPlacementSurfaceLowDistorsionMapping
        {
            [Tooltip("The triangle index to read from.")]
            public uint triangle = 0u;

            [Tooltip("Low distortion mapping coordinate.")]
            public Vector2 square;
        }

        public class InputPropertiesEdge
        {
            [Tooltip("The start index of edge, line will be renderer with the following one.")]
            public uint index = 0u;

            [Tooltip("Linear interpolation value between start and end edge position.")]
            public float x;
        }

        [Flags]
        public enum VertexAttributeFlag
        {
            None = 0,
            Position = 1 << 0,
            Normal = 1 << 1,
            Tangent = 1 << 2,
            Color = 1 << 3,
            TexCoord0 = 1 << 4,
            TexCoord1 = 1 << 5,
            TexCoord2 = 1 << 6,
            TexCoord3 = 1 << 7,
            TexCoord4 = 1 << 8,
            TexCoord5 = 1 << 9,
            TexCoord6 = 1 << 10,
            TexCoord7 = 1 << 11,
            BlendWeight = 1 << 12,
            BlendIndices = 1 << 13
        }

        public enum SourceType
        {
            Mesh,
            SkinnedMeshRenderer
        }

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField, Tooltip("Outputs the result of the Mesh sampling operation.")]
        private VertexAttributeFlag output = VertexAttributeFlag.Position | VertexAttributeFlag.Color | VertexAttributeFlag.TexCoord0;

        [VFXSetting, SerializeField, Tooltip("Specifies how Unity handles the sample when the custom vertex index is out the out of bounds of the vertex array.")]
        private VFXOperatorUtility.SequentialAddressingMode mode = VFXOperatorUtility.SequentialAddressingMode.Clamp;

        [VFXSetting, SerializeField, Tooltip("Change what kind of primitive we want to sample.")]
        private PlacementMode placementMode = PlacementMode.Vertex;

        [VFXSetting, SerializeField, Tooltip("Surface sampling coordinate.")]
        private SurfaceCoordinates surfaceCoordinates = SurfaceCoordinates.Uniform;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), SerializeField, Tooltip("Choose between classic mesh sampling or skinned renderer mesh sampling.")]
        private SourceType source = SourceType.Mesh;

        private bool HasOutput(VertexAttributeFlag flag)
        {
            return (output & flag) == flag;
        }

        private IEnumerable<VertexAttributeFlag> GetOutputVertexAttributes()
        {
            var vertexAttributes = Enum.GetValues(typeof(VertexAttributeFlag)).Cast<VertexAttributeFlag>();
            foreach (var vertexAttribute in vertexAttributes)
                if (vertexAttribute != VertexAttributeFlag.None && HasOutput(vertexAttribute))
                    yield return vertexAttribute;
        }

        private static Type GetOutputType(VertexAttributeFlag attribute)
        {
            switch (attribute)
            {
                case VertexAttributeFlag.Position: return typeof(Vector3);
                case VertexAttributeFlag.Normal: return typeof(Vector3);
                case VertexAttributeFlag.Tangent: return typeof(Vector4);
                case VertexAttributeFlag.Color: return typeof(Vector4);
                case VertexAttributeFlag.TexCoord0:
                case VertexAttributeFlag.TexCoord1:
                case VertexAttributeFlag.TexCoord2:
                case VertexAttributeFlag.TexCoord3:
                case VertexAttributeFlag.TexCoord4:
                case VertexAttributeFlag.TexCoord5:
                case VertexAttributeFlag.TexCoord6:
                case VertexAttributeFlag.TexCoord7: return typeof(Vector4);
                case VertexAttributeFlag.BlendWeight: return typeof(Vector4);
                case VertexAttributeFlag.BlendIndices: return typeof(Vector4);
                default: throw new InvalidOperationException("Unexpected attribute : " + attribute);
            }
        }

        private static VertexAttribute GetActualVertexAttribute(VertexAttributeFlag attribute)
        {
            switch (attribute)
            {
                case VertexAttributeFlag.Position: return VertexAttribute.Position;
                case VertexAttributeFlag.Normal: return VertexAttribute.Normal;
                case VertexAttributeFlag.Tangent: return VertexAttribute.Tangent;
                case VertexAttributeFlag.Color: return VertexAttribute.Color;
                case VertexAttributeFlag.TexCoord0: return VertexAttribute.TexCoord0;
                case VertexAttributeFlag.TexCoord1: return VertexAttribute.TexCoord1;
                case VertexAttributeFlag.TexCoord2: return VertexAttribute.TexCoord2;
                case VertexAttributeFlag.TexCoord3: return VertexAttribute.TexCoord3;
                case VertexAttributeFlag.TexCoord4: return VertexAttribute.TexCoord4;
                case VertexAttributeFlag.TexCoord5: return VertexAttribute.TexCoord5;
                case VertexAttributeFlag.TexCoord6: return VertexAttribute.TexCoord6;
                case VertexAttributeFlag.TexCoord7: return VertexAttribute.TexCoord7;
                case VertexAttributeFlag.BlendWeight: return VertexAttribute.BlendWeight;
                case VertexAttributeFlag.BlendIndices: return VertexAttribute.BlendIndices;
                default: throw new InvalidOperationException("Unexpected attribute : " + attribute);
            }
        }

        protected override IEnumerable<string> filteredOutSettings
        {
            get
            {
                foreach (var settings in base.filteredOutSettings)
                    yield return settings;
                if (placementMode != PlacementMode.Surface)
                    yield return "surfaceCoordinates";
            }
    }

        protected sealed override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get
            {
                var props = source == SourceType.Mesh ? PropertiesFromType("InputPropertiesMesh") : PropertiesFromType("InputPropertiesSkinnedMeshRenderer");
                if (placementMode == PlacementMode.Vertex)
                {
                    props = props.Concat(PropertiesFromType("InputPropertiesPlacementVertex"));
                }
                else if (placementMode == PlacementMode.Surface)
                {
                    if (surfaceCoordinates == SurfaceCoordinates.Barycentric)
                        props = props.Concat(PropertiesFromType("InputPropertiesPlacementSurfaceBarycentricCoordinates"));
                    else
                        props = props.Concat(PropertiesFromType("InputPropertiesPlacementSurfaceLowDistorsionMapping"));
                }
                else if (placementMode == PlacementMode.Edge)
                {
                    props = props.Concat(PropertiesFromType("InputPropertiesEdge"));
                }
                return props;
            }
        }

        protected override IEnumerable<VFXPropertyWithValue> outputProperties
        {
            get
            {
                foreach (var vertexAttribute in GetOutputVertexAttributes())
                {
                    var outputType = GetOutputType(vertexAttribute);
                    yield return new VFXPropertyWithValue(new VFXProperty(outputType, vertexAttribute.ToString()));
                }
            }
        }

        private void SampleVertex(VFXExpression sourceMesh, VFXExpression meshVertexCount, VFXExpression vertexIndex, List<VFXExpression> sampledValues)
        {
            var mesh = source == SourceType.Mesh ? sourceMesh : new VFXExpressionMeshFromSkinnedMeshRenderer(sourceMesh);

            foreach (var vertexAttribute in GetOutputVertexAttributes())
            {
                var channelIndex = VFXValue.Constant<uint>((uint)GetActualVertexAttribute(vertexAttribute));

                var meshVertexStride = new VFXExpressionMeshVertexStride(mesh, channelIndex);
                var meshChannelOffset = new VFXExpressionMeshChannelOffset(mesh, channelIndex);

                var outputType = GetOutputType(vertexAttribute);
                VFXExpression sampled = null;

                var meshChannelFormatAndDimension = new VFXExpressionMeshChannelInfos(mesh, channelIndex);
                var vertexOffset = vertexIndex * meshVertexStride + meshChannelOffset;

                if (source == SourceType.Mesh)
                {
                    if (vertexAttribute == VertexAttributeFlag.Color)
                        sampled = new VFXExpressionSampleMeshColor(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                    else if (outputType == typeof(float))
                        sampled = new VFXExpressionSampleMeshFloat(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                    else if (outputType == typeof(Vector2))
                        sampled = new VFXExpressionSampleMeshFloat2(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                    else if (outputType == typeof(Vector3))
                        sampled = new VFXExpressionSampleMeshFloat3(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                    else
                        sampled = new VFXExpressionSampleMeshFloat4(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                }
                else
                {
                    if (vertexAttribute == VertexAttributeFlag.Color)
                        sampled = new VFXExpressionSampleSkinnedMeshRendererColor(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                    else if (outputType == typeof(float))
                        sampled = new VFXExpressionSampleSkinnedMeshRendererFloat(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                    else if (outputType == typeof(Vector2))
                        sampled = new VFXExpressionSampleSkinnedMeshRendererFloat2(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                    else if (outputType == typeof(Vector3))
                        sampled = new VFXExpressionSampleSkinnedMeshRendererFloat3(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                    else
                        sampled = new VFXExpressionSampleSkinnedMeshRendererFloat4(sourceMesh, vertexOffset, meshChannelFormatAndDimension);
                }
                sampledValues.Add(sampled);
            }
        }

        protected override sealed VFXExpression[] BuildExpression(VFXExpression[] inputExpression)
        {
            var sourceMesh = inputExpression[0];
            var mesh = source == SourceType.Mesh ? sourceMesh : new VFXExpressionMeshFromSkinnedMeshRenderer(sourceMesh);

            var meshVertexCount = new VFXExpressionMeshVertexCount(mesh);
            var meshIndexCount = new VFXExpressionMeshIndexCount(mesh);
            var meshIndexFormat = new VFXExpressionMeshIndexFormat(mesh);

            var outputExpressions = new List<VFXExpression>();
            if (placementMode == PlacementMode.Vertex)
            {
                var vertexIndex = VFXOperatorUtility.ApplyAddressingMode(inputExpression[1], meshVertexCount, mode);
                SampleVertex(sourceMesh, meshVertexCount, vertexIndex, outputExpressions);
            }
            else if (placementMode == PlacementMode.Edge)
            {
                var oneInt = VFXOperatorUtility.OneExpression[UnityEngine.VFX.VFXValueType.Int32];
                var oneUint = VFXOperatorUtility.OneExpression[UnityEngine.VFX.VFXValueType.Uint32];
                var threeUint = VFXValue.Constant(3u);

                var baseIndex = VFXOperatorUtility.ApplyAddressingMode(inputExpression[1], meshIndexCount, mode);
                var nextIndex = baseIndex + oneUint;

                //Loop triangle
                var loop = VFXOperatorUtility.Modulo(nextIndex, threeUint);
                var predicat = new VFXExpressionCondition(VFXCondition.NotEqual, new VFXExpressionCastUintToFloat(loop), VFXOperatorUtility.ZeroExpression[UnityEngine.VFX.VFXValueType.Float]);
                nextIndex = new VFXExpressionBranch(predicat, nextIndex, nextIndex - threeUint);

                var sampledIndex_A = new VFXExpressionSampleIndex(mesh, baseIndex, meshIndexFormat);
                var sampledIndex_B = new VFXExpressionSampleIndex(mesh, nextIndex, meshIndexFormat);

                var allInputValues = new List<VFXExpression>();
                SampleVertex(sourceMesh, meshVertexCount, sampledIndex_A, allInputValues);
                SampleVertex(sourceMesh, meshVertexCount, sampledIndex_B, allInputValues);

                var attributeCount = GetOutputVertexAttributes().Count();
                for (int i = 0; i < attributeCount; ++i)
                {
                    var sampleValue_A = allInputValues[i];
                    var sampleValue_B = allInputValues[i + attributeCount];

                    var outputValueType = sampleValue_A.valueType;
                    var s = VFXOperatorUtility.CastFloat(inputExpression[2], outputValueType);
                    outputExpressions.Add(VFXOperatorUtility.Lerp(sampleValue_A, sampleValue_B, s));
                }
            }
            else if (placementMode == PlacementMode.Surface)
            {
                var UintThree = VFXValue.Constant<uint>(3u);
                var triangleCount = meshIndexCount / UintThree;
                var triangleIndex = VFXOperatorUtility.ApplyAddressingMode(inputExpression[1], triangleCount, mode);
                var baseIndex = triangleIndex * UintThree;

                var sampledIndex_A = new VFXExpressionSampleIndex(mesh, baseIndex, meshIndexFormat);
                var sampledIndex_B = new VFXExpressionSampleIndex(mesh, baseIndex + VFXValue.Constant<uint>(1u), meshIndexFormat);
                var sampledIndex_C = new VFXExpressionSampleIndex(mesh, baseIndex + VFXValue.Constant<uint>(2u), meshIndexFormat);

                var allInputValues = new List<VFXExpression>();
                SampleVertex(sourceMesh, meshVertexCount, sampledIndex_A, allInputValues);
                SampleVertex(sourceMesh, meshVertexCount, sampledIndex_B, allInputValues);
                SampleVertex(sourceMesh, meshVertexCount, sampledIndex_C, allInputValues);

                var attributeCount = GetOutputVertexAttributes().Count();
                for (int i = 0; i < attributeCount; ++i)
                {
                    var sampleValue_A = allInputValues[i];
                    var sampleValue_B = allInputValues[i + attributeCount];
                    var sampleValue_C = allInputValues[i + attributeCount*2];
                    var outputValueType = sampleValue_A.valueType;

                    VFXExpression barycentricCoordinates = null;
                    var one = VFXOperatorUtility.OneExpression[UnityEngine.VFX.VFXValueType.Float];
                    if (surfaceCoordinates == SurfaceCoordinates.Barycentric)
                    {
                        var barycentricCoordinateInput = inputExpression[2];
                        barycentricCoordinates = new VFXExpressionCombine(barycentricCoordinateInput.x, barycentricCoordinateInput.y, one - barycentricCoordinateInput.x - barycentricCoordinateInput.y);
                    }
                    else if (surfaceCoordinates == SurfaceCoordinates.Uniform)
                    {
                        //https://hal.archives-ouvertes.fr/hal-02073696v2/document
                        var input = inputExpression[2];

                        var half2 = VFXOperatorUtility.HalfExpression[UnityEngine.VFX.VFXValueType.Float2];
                        var zero = VFXOperatorUtility.ZeroExpression[UnityEngine.VFX.VFXValueType.Float];
                        var t = input * half2;
                        var offset = t.y - t.x;
                        var pred = new VFXExpressionCondition(VFXCondition.Greater, offset, zero);
                        var t2 = new VFXExpressionBranch(pred, t.y + offset, t.y);
                        var t1 = new VFXExpressionBranch(pred, t.x, t.x - offset);
                        var t3 = one - t2 - t1;
                        barycentricCoordinates = new VFXExpressionCombine(t1, t2, t3);

                        /* Possible variant See http://inis.jinr.ru/sl/vol1/CMC/Graphics_Gems_1,ed_A.Glassner.pdf (p24) uniform distribution from two numbers in triangle generating barycentric coordinate
                        var input = VFXOperatorUtility.Saturate(inputExpression[2]);
                        var s = input.x;
                        var t = VFXOperatorUtility.Sqrt(input.y);
                        var a = one - t;
                        var b = (one - s) * t;
                        var c = s * t;
                        barycentricCoordinates = new VFXExpressionCombine(a, b, c);
                        */
                    }
                    else
                    {
                        throw new Exception("No supported surfaceCoordinates : " + surfaceCoordinates);
                    }

                    var barycentricCoordinateX = VFXOperatorUtility.CastFloat(barycentricCoordinates.x, outputValueType);
                    var barycentricCoordinateY = VFXOperatorUtility.CastFloat(barycentricCoordinates.y, outputValueType);
                    var barycentricCoordinateZ = VFXOperatorUtility.CastFloat(barycentricCoordinates.z, outputValueType);

                    var r = sampleValue_A * barycentricCoordinateX + sampleValue_B * barycentricCoordinateY + sampleValue_C * barycentricCoordinateZ;
                    outputExpressions.Add(r);
                }
            }
            return outputExpressions.ToArray();
        }
    }
}
