﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Sounds;
using CUE4Parse_Conversion.Textures;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Material.Editor;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Sound;
using CUE4Parse.UE4.Assets.Exports.Sound.Node;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.Engine.Animation;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.Utils;
using FortnitePorting.AppUtils;
using FortnitePorting.Views.Controls;
using FortnitePorting.Views.Extensions;
using MercuryCommons.Utilities.Extensions;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SkiaSharp;

namespace FortnitePorting.Exports;

public static class ExportHelpers
{
    public static List<ExportPart> CharacterParts(IEnumerable<UObject> inputParts, List<ExportMesh> exportMeshes)
    {
        var exportParts = new List<ExportPart>();
        var headMorphType = ECustomHatType.None;
        var headMorphNames = new Dictionary<ECustomHatType, string>();
        FLinearColor? skinColor = null;
        foreach (var part in inputParts)
        {
            var skeletalMesh = part.GetOrDefault<USkeletalMesh?>("SkeletalMesh");
            if (skeletalMesh is null) continue;

            if (!skeletalMesh.TryConvert(out var convertedMesh)) continue;
            if (convertedMesh.LODs.Count <= 0) continue;

            var exportPart = new ExportPart();
            exportPart.MeshPath = skeletalMesh.GetPathName();
            Save(skeletalMesh);

            exportPart.NumLods = convertedMesh.LODs.Count;

            var characterPartType = part.GetOrDefault<EFortCustomPartType>("CharacterPartType");
            exportPart.Part = characterPartType.ToString();

            var genderPermitted = part.GetOrDefault("GenderPermitted", EFortCustomGender.Male);
            exportPart.GenderPermitted = genderPermitted;

            if (part.TryGetValue<UObject>(out var additionalData, "AdditionalData"))
            {
                var socketName = additionalData.GetOrDefault<FName?>("AttachSocketName");
                var attachToSocket = part.GetOrDefault("bAttachToSocket", true);
                if (attachToSocket)
                    exportPart.SocketName = socketName?.Text;

                if (additionalData.TryGetValue(out FName hatType, "HatType"))
                {
                    Enum.TryParse(hatType.Text.Replace("ECustomHatType::ECustomHatType_", string.Empty), out headMorphType);
                }

                if (additionalData.ExportType.Equals("CustomCharacterHeadData"))
                {
                    foreach (var type in Enum.GetValues<ECustomHatType>())
                    {
                        if (additionalData.TryGetValue(out FName[] morphNames, type + "MorphTargets"))
                        {
                            headMorphNames[type] = morphNames[0].Text;
                        }
                    }

                    if (additionalData.TryGetValue(out UObject skinColorSwatch, "SkinColorSwatch"))
                    {
                        var colorPairs = skinColorSwatch.GetOrDefault("ColorPairs", Array.Empty<FStructFallback>());
                        var skinColorPair = colorPairs.FirstOrDefault(x => x.Get<FName>("ColorName").Text.Equals("Skin Boost Color and Exponent", StringComparison.OrdinalIgnoreCase));
                        if (skinColorPair is not null) skinColor = skinColorPair.Get<FLinearColor>("ColorValue");
                    }

                    if (additionalData.TryGetValue(out UAnimBlueprintGeneratedClass animBlueprint, "AnimClass") && animBlueprint.ClassDefaultObject is not null)
                    {
                        var bagel = AppVM.CUE4ParseVM.Provider.LoadAllObjects(animBlueprint
                            .GetPathName().SubstringBeforeLast("."));
                        var egg = JsonConvert.SerializeObject(bagel);
                        var classDefaultObject = animBlueprint.ClassDefaultObject.Load();
                        if (classDefaultObject?.TryGetValue(out FStructFallback poseAssetNode, "AnimGraphNode_PoseBlendNode") ?? false)
                        {
                            var poseAsset = poseAssetNode.Get<UPoseAsset>("PoseAsset");
                            exportPart.ProcessPoses(skeletalMesh, poseAsset);
                        }
                        /*else if (skeletalMesh.ReferenceSkeleton.FinalRefBoneInfo.Any(bone => bone.Name.Text.Equals("FACIAL_C_FacialRoot", StringComparison.OrdinalIgnoreCase)))
                        {
                            exportPart.ProcessMetahumanPoses();
                        }*/
                    }
                }
            }

            var sections = convertedMesh.LODs[0].Sections.Value;
            for (var idx = 0; idx < sections.Length; idx++)
            {
                var section = sections[idx];
                if (section.Material is null) continue;

                if (!section.Material.TryLoad(out var materialObject)) continue;
                if (materialObject is not UMaterialInterface material) continue;

                var exportMaterial = CreateExportMaterial(material, idx);
                exportPart.Materials.Add(exportMaterial);
            }

            if (part.TryGetValue(out FStructFallback[] materialOverrides, "MaterialOverrides"))
            {
                OverrideMaterials(materialOverrides, ref exportPart);
            }

            exportParts.Add(exportPart);
        }

        var headPart = exportParts.FirstOrDefault(x => x.Part.Equals("Head"));
        var bodyPart = exportParts.FirstOrDefault(x => x.Part.Equals("Body"));
        var facePart = exportParts.FirstOrDefault(x => x.Part.Equals("Face"));
        if (headMorphType != ECustomHatType.None && headMorphNames.ContainsKey(headMorphType) && headPart is not null)
        {
            headPart.MorphName = headMorphNames[headMorphType];
        }

        if (headPart is not null && facePart is not null)
        {
            facePart.PoseNames = headPart.PoseNames;
            facePart.PoseAnimation = headPart.PoseAnimation;
        }

        if (skinColor is not null && bodyPart is not null)
        {
            foreach (var material in bodyPart.Materials)
            {
                var foundSkinColor = material.Vectors.FirstOrDefault(x => x.Name.Equals("Skin Boost Color And Exponent"));
                if (foundSkinColor is not null)
                {
                    foundSkinColor.Value = skinColor.Value;
                }
                else
                {
                    material.Vectors.Add(new VectorParameter("Skin Boost Color And Exponent", skinColor.Value));
                }
            }
        }

        exportMeshes.AddRange(exportParts);
        return exportParts;
    }

    public static void Weapon(UObject weaponDefinition, List<ExportMesh> exportParts)
    {
        var weapons = GetWeaponMeshes(weaponDefinition);
        foreach (var weapon in weapons)
        {
            switch (weapon)
            {
                case UStaticMesh staticMesh:
                    Mesh(staticMesh, exportParts);
                    break;
                case USkeletalMesh skeletalMesh:
                    Mesh(skeletalMesh, exportParts);
                    break;
            }
        }
    }

    public static List<UObject?> GetWeaponMeshes(UObject weaponDefinition)
    {
        var weapons = new List<UObject?>();
        USkeletalMesh? mainSkeletalMesh = null;
        mainSkeletalMesh = weaponDefinition.GetOrDefault("PickupSkeletalMesh", mainSkeletalMesh);
        mainSkeletalMesh = weaponDefinition.GetOrDefault("WeaponMeshOverride", mainSkeletalMesh);
        weapons.AddIfNotNull(mainSkeletalMesh);

        if (mainSkeletalMesh is null)
        {
            weaponDefinition.TryGetValue(out UStaticMesh? mainStaticMesh, "PickupStaticMesh");
            weapons.AddIfNotNull(mainStaticMesh);
        }

        weaponDefinition.TryGetValue(out USkeletalMesh? offHandMesh, "WeaponMeshOffhandOverride");
        weapons.AddIfNotNull(offHandMesh);

        if (weapons.Count > 0) return weapons;

        if (weaponDefinition.TryGetValue(out UBlueprintGeneratedClass blueprint, "WeaponActorClass"))
        {
            var defaultObject = blueprint.ClassDefaultObject.Load()!;
            if (defaultObject.TryGetValue(out UObject weaponMeshData, "WeaponMesh"))
            {
                weapons.AddIfNotNull(weaponMeshData.GetOrDefault<USkeletalMesh>("SkeletalMesh"));
            }

            if (defaultObject.TryGetValue(out UObject leftWeaponMeshData, "LeftHandWeaponMesh"))
            {
                weapons.AddIfNotNull(leftWeaponMeshData.GetOrDefault<USkeletalMesh>("SkeletalMesh"));
            }
        }

        return weapons;
    }

    public static void Mesh<T>(USkeletalMesh? skeletalMesh, List<T> exportParts) where T : ExportMesh, new()
    {
        if (skeletalMesh is null) return;
        if (!skeletalMesh.TryConvert(out var convertedMesh)) return;
        if (convertedMesh.LODs.Count <= 0) return;

        var exportPart = new T();
        exportPart.MeshPath = skeletalMesh.GetPathName();
        Save(skeletalMesh);

        exportPart.NumLods = convertedMesh.LODs.Count;

        var sections = convertedMesh.LODs[0].Sections.Value;
        for (var idx = 0; idx < sections.Length; idx++)
        {
            var section = sections[idx];
            if (section.Material is null) continue;

            if (!section.Material.TryLoad(out var materialObject)) continue;
            if (materialObject is not UMaterialInterface material) continue;

            var exportMaterial = CreateExportMaterial(material, idx);
            exportPart.Materials.Add(exportMaterial);
        }

        exportParts.Add(exportPart);
    }

    public static void Mesh<T>(UStaticMesh? staticMesh, List<T> exportParts) where T : ExportMesh, new()
    {
        if (staticMesh is null) return;
        if (!staticMesh.TryConvert(out var convertedMesh)) return;
        if (convertedMesh.LODs.Count <= 0) return;

        var exportPart = new T();
        exportPart.MeshPath = staticMesh.GetPathName();
        Save(staticMesh);

        exportPart.NumLods = convertedMesh.LODs.Count;

        var sections = convertedMesh.LODs[0].Sections.Value;
        for (var idx = 0; idx < sections.Length; idx++)
        {
            var section = sections[idx];
            if (section.Material is null) continue;


            if (!section.Material.TryLoad(out var materialObject)) continue;
            if (materialObject is not UMaterialInterface material) continue;

            var exportMaterial = CreateExportMaterial(material, idx);
            exportPart.Materials.Add(exportMaterial);
        }

        exportParts.Add(exportPart);
    }

    public static void OverrideMaterials(FStructFallback[] overrides, ref ExportPart exportPart)
    {
        foreach (var materialOverride in overrides)
        {
            var overrideMaterial = materialOverride.Get<FSoftObjectPath>("OverrideMaterial");
            if (!overrideMaterial.TryLoad(out var materialObject)) continue;
            if (materialObject is not UMaterialInterface material) continue;

            var exportMaterial = CreateExportMaterial<ExportMaterialOverride>(material, materialOverride.Get<int>("MaterialOverrideIndex"));
            exportMaterial.MaterialNameToSwap = materialOverride.GetOrDefault<FSoftObjectPath>("MaterialToSwap").AssetPathName.Text.SubstringAfterLast(".");

            if (exportPart.Materials.Count == 0)
            {
                exportPart.OverrideMaterials.Add(exportMaterial);
            }

            for (var idx = 0; idx < exportPart.Materials.Count; idx++)
            {
                if (exportMaterial.SlotIndex >= exportPart.Materials.Count) continue;
                if (exportPart.Materials[exportMaterial.SlotIndex].Hash == exportPart.Materials[idx].Hash)
                {
                    exportPart.OverrideMaterials.Add(exportMaterial with { SlotIndex = idx });
                }
            }
        }
    }

    public static void OverrideMaterials(FStructFallback[] overrides, List<ExportMaterial> exportMaterials)
    {
        foreach (var materialOverride in overrides)
        {
            var overrideMaterial = materialOverride.Get<FSoftObjectPath>("OverrideMaterial");
            if (!overrideMaterial.TryLoad(out var materialObject)) continue;
            if (materialObject is not UMaterialInterface material) continue;

            var exportMaterial = CreateExportMaterial<ExportMaterialOverride>(material, materialOverride.Get<int>("MaterialOverrideIndex"));
            exportMaterial.MaterialNameToSwap = materialOverride.GetOrDefault<FSoftObjectPath>("MaterialToSwap").AssetPathName.Text.SubstringAfterLast(".");

            exportMaterials.Add(exportMaterial);
        }
    }

    public static (List<TextureParameter>, List<ScalarParameter>, List<VectorParameter>, List<SwitchParameter>, List<ComponentMaskParameter>) MaterialParameters(UMaterialInstanceConstant materialInstance)
    {
        var textures = new List<TextureParameter>();
        foreach (var parameter in materialInstance.TextureParameterValues)
        {
            if (!parameter.ParameterValue.TryLoad(out UTexture2D texture)) continue;
            textures.Add(new TextureParameter(parameter.Name, texture.GetPathName(), texture.SRGB, texture.CompressionSettings));
            Save(texture);
        }

        var scalars = new List<ScalarParameter>();
        foreach (var parameter in materialInstance.ScalarParameterValues)
        {
            scalars.Add(new ScalarParameter(parameter.Name, parameter.ParameterValue));
        }

        var vectors = new List<VectorParameter>();
        foreach (var parameter in materialInstance.VectorParameterValues)
        {
            if (parameter.ParameterValue is null) continue;
            vectors.Add(new VectorParameter(parameter.Name, parameter.ParameterValue.Value));
        }

        var switches = new List<SwitchParameter>();
        var componentMasks = new List<ComponentMaskParameter>();
        if (materialInstance.StaticParameters is not null)
        {
            foreach (var parameter in materialInstance.StaticParameters.StaticSwitchParameters)
            {
                if (parameter.ParameterInfo is null) continue;
                switches.Add(new SwitchParameter(parameter.Name, parameter.Value));
            }

            foreach (var parameter in materialInstance.StaticParameters.StaticComponentMaskParameters)
            {
                if (parameter.ParameterInfo is null) continue;
                componentMasks.Add(new ComponentMaskParameter(parameter.Name, parameter.ToLinearColor()));
            }
        }

        if (materialInstance.TryLoadEditorData<UMaterialInstanceEditorOnlyData>(out var materialInstanceEditorData) && materialInstanceEditorData?.StaticParameters is not null)
        {
            foreach (var parameter in materialInstanceEditorData.StaticParameters.StaticSwitchParameters)
            {
                if (parameter.ParameterInfo is null) continue;
                switches.AddUnique(new SwitchParameter(parameter.Name, parameter.Value));
            }

            foreach (var parameter in materialInstanceEditorData.StaticParameters.StaticComponentMaskParameters)
            {
                if (parameter.ParameterInfo is null) continue;
                componentMasks.AddUnique(new ComponentMaskParameter(parameter.Name, parameter.ToLinearColor()));
            }
        }

        if (materialInstance.Parent is UMaterialInstanceConstant materialParent)
        {
            var (parentTextures, parentScalars, parentVectors, parentSwitches, parentComponentMasks) = MaterialParameters(materialParent);
            foreach (var parentTexture in parentTextures)
            {
                if (textures.Any(x => x.Name.Equals(parentTexture.Name))) continue;
                textures.Add(parentTexture);
            }

            foreach (var parentScalar in parentScalars)
            {
                if (scalars.Any(x => x.Name.Equals(parentScalar.Name))) continue;
                scalars.Add(parentScalar);
            }

            foreach (var parentVector in parentVectors)
            {
                if (vectors.Any(x => x.Name.Equals(parentVector.Name))) continue;
                vectors.Add(parentVector);
            }

            foreach (var parentSwitch in parentSwitches)
            {
                if (switches.Any(x => x.Name.Equals(parentSwitch.Name))) continue;
                switches.Add(parentSwitch);
            }

            foreach (var parentComponentMask in parentComponentMasks)
            {
                if (componentMasks.Any(x => x.Name.Equals(parentComponentMask.Name))) continue;
                componentMasks.Add(parentComponentMask);
            }
        }
        else if (materialInstance.Parent is UMaterialInterface materialInterface)
        {
            var (parentTextures, parentScalars, parentVectors) = MaterialParameters(materialInterface);
            
            foreach (var parentTexture in parentTextures)
            {
                if (textures.Any(x => x.Name.Equals(parentTexture.Name))) continue;
                textures.Add(parentTexture);
            }

            foreach (var parentScalar in parentScalars)
            {
                if (scalars.Any(x => x.Name.Equals(parentScalar.Name))) continue;
                scalars.Add(parentScalar);
            }

            foreach (var parentVector in parentVectors)
            {
                if (vectors.Any(x => x.Name.Equals(parentVector.Name))) continue;
                vectors.Add(parentVector);
            }
        }

        var parameters = new CMaterialParams2();
        materialInstance.GetParams(parameters, EMaterialFormat.AllLayers);

        if (parameters.TryGetTexture2d(out var diffuseTexture, CMaterialParams2.Diffuse[0]))
        {
            Save(diffuseTexture);
            textures.Add(new TextureParameter("Diffuse", diffuseTexture.GetPathName(), diffuseTexture.SRGB, diffuseTexture.CompressionSettings));
        }

        if (parameters.TryGetTexture2d(out var specularMasksTexture, CMaterialParams2.SpecularMasks[0]))
        {
            Save(specularMasksTexture);
            textures.Add(new TextureParameter("SpecularMasks", specularMasksTexture.GetPathName(), specularMasksTexture.SRGB, specularMasksTexture.CompressionSettings));
        }

        if (parameters.TryGetTexture2d(out var normalsTexture, CMaterialParams2.Normals[0]))
        {
            Save(normalsTexture);
            textures.Add(new TextureParameter("Normals", normalsTexture.GetPathName(), normalsTexture.SRGB, normalsTexture.CompressionSettings));
        }

        return (textures, scalars, vectors, switches, componentMasks);
    }

    public static (List<TextureParameter>, List<ScalarParameter>, List<VectorParameter>) MaterialParameters(UMaterialInterface materialInterface)
    {
        var parameters = new CMaterialParams2();
        materialInterface.GetParams(parameters, EMaterialFormat.AllLayers);

        var textures = new List<TextureParameter>();
        foreach (var (name, value) in parameters.Textures)
        {
            if (value is UTexture2D texture)
            {
                Save(texture);
                textures.Add(new TextureParameter(name, texture.GetPathName(), texture.SRGB, texture.CompressionSettings));
                break;
            }
        }

        if (materialInterface.TryLoadEditorData<UMaterialEditorOnlyData>(out var materialEditorData) && materialEditorData is not null)
        {
            bool TryLoadInput(UObject obj, string name, out UObject expression)
            {
                expression = obj;
                return obj.TryGetValue(out FExpressionInput input, name) && input.Expression.TryLoad(out expression!);
            }
            
            bool TryAddExpressionTexture(string expressionName, string paramName)
            {
                if (!TryLoadInput(materialEditorData, expressionName, out var expression)) return false;
                if (expression.ExportType == "MaterialExpressionNamedRerouteDeclaration") TryLoadInput(expression, "Input", out expression);
                if (!expression.TryGetValue(out UTexture2D texture, "Texture")) return false;

                textures.AddUnique(new TextureParameter(paramName, texture.GetPathName(), texture.SRGB, texture.CompressionSettings));
                Save(texture);
                return true;
            }

            TryAddExpressionTexture("BaseColor", "Diffuse");
            TryAddExpressionTexture("Specular", "SpecularMasks");
            TryAddExpressionTexture("Metallic", "SpecularMasks");
            TryAddExpressionTexture("Roughness", "SpecularMasks");
            TryAddExpressionTexture("Normal", "Normals");
            TryAddExpressionTexture("EmissiveColor", "Emissive");
        }

        return (textures, new List<ScalarParameter>(), new List<VectorParameter>());
    }

    public static (List<TextureParameter>, List<ScalarParameter>, List<VectorParameter>) MaterialParametersOverride(FStructFallback data)
    {
        var textures = new List<TextureParameter>();
        foreach (var parameter in data.GetOrDefault("TextureParams", Array.Empty<FStructFallback>()))
        {
            if (!parameter.TryGetValue(out UTexture2D texture, "Value")) continue;
            textures.Add(new TextureParameter(parameter.Get<FName>("ParamName").Text, texture.GetPathName(), texture.SRGB, texture.CompressionSettings));
            Save(texture);
        }

        var scalars = new List<ScalarParameter>();
        foreach (var parameter in data.GetOrDefault("FloatParams", Array.Empty<FStructFallback>()))
        {
            var scalar = parameter.Get<float>("Value");
            scalars.Add(new ScalarParameter(parameter.Get<FName>("ParamName").Text, scalar));
        }

        var vectors = new List<VectorParameter>();
        foreach (var parameter in data.GetOrDefault("ColorParams", Array.Empty<FStructFallback>()))
        {
            if (!parameter.TryGetValue(out FLinearColor color, "Value")) continue;
            vectors.Add(new VectorParameter(parameter.Get<FName>("ParamName").Text, color));
        }

        return (textures, scalars, vectors);
    }

    public static ExportMesh? Mesh(UStaticMesh? skeletalMesh)
    {
        return Mesh<ExportMesh>(skeletalMesh);
    }

    public static T? Mesh<T>(UStaticMesh? staticMesh) where T : ExportMesh, new()
    {
        if (staticMesh is null) return null;
        if (!staticMesh.TryConvert(out var convertedMesh)) return null;
        if (convertedMesh.LODs.Count <= 0) return null;

        var exportMesh = new T();
        exportMesh.MeshPath = staticMesh.GetPathName();
        Save(staticMesh);

        exportMesh.NumLods = convertedMesh.LODs.Count;

        var sections = convertedMesh.LODs[0].Sections.Value;
        for (var idx = 0; idx < sections.Length; idx++)
        {
            var section = sections[idx];
            if (section.Material is null) continue;
            if (!section.Material.TryLoad(out var materialObject)) continue;
            if (materialObject is not UMaterialInterface material) continue;

            var exportMaterial = CreateExportMaterial(material, idx);
            exportMesh.Materials.Add(exportMaterial);
        }

        return exportMesh;
    }

    public static ExportMesh? Mesh(USkeletalMesh? skeletalMesh)
    {
        return Mesh<ExportMesh>(skeletalMesh);
    }

    public static T? Mesh<T>(USkeletalMesh? skeletalMesh) where T : ExportMesh, new()
    {
        if (skeletalMesh is null) return null;
        if (!skeletalMesh.TryConvert(out var convertedMesh)) return null;
        if (convertedMesh.LODs.Count <= 0) return null;

        var exportMesh = new T();
        exportMesh.MeshPath = skeletalMesh.GetPathName();
        Save(skeletalMesh);

        exportMesh.NumLods = convertedMesh.LODs.Count;

        var sections = convertedMesh.LODs[0].Sections.Value;
        for (var idx = 0; idx < sections.Length; idx++)
        {
            var section = sections[idx];
            if (section.Material is null) continue;

            if (!section.Material.TryLoad(out var materialObject)) continue;
            if (materialObject is not UMaterialInterface material) continue;

            var exportMaterial = CreateExportMaterial(material, idx);
            exportMesh.Materials.Add(exportMaterial);
        }

        return exportMesh;
    }

    public static void OverrideMeshes(FStructFallback[] overrides, List<ExportMeshOverride> exportMeshes)
    {
        foreach (var meshOverride in overrides)
        {
            var meshToSwap = meshOverride.Get<FSoftObjectPath>("MeshToSwap");
            var meshToOverride = meshOverride.Get<USkeletalMesh>("OverrideMesh");

            var overrideMeshExport = Mesh<ExportMeshOverride>(meshToOverride);
            if (overrideMeshExport is null) continue;

            overrideMeshExport.MeshToSwap = meshToSwap.AssetPathName.Text;
            exportMeshes.Add(overrideMeshExport);
        }
    }

    public static void OverrideParameters(FStructFallback[] overrides, List<ExportMaterialParams> exportParams)
    {
        foreach (var paramData in overrides)
        {
            var exportMaterialParams = new ExportMaterialParams();
            exportMaterialParams.MaterialToAlter = paramData.Get<FSoftObjectPath>("MaterialToAlter").AssetPathName.Text;
            if (string.IsNullOrEmpty(exportMaterialParams.MaterialToAlter)) continue;
             
            (exportMaterialParams.Textures, exportMaterialParams.Scalars, exportMaterialParams.Vectors) = MaterialParametersOverride(paramData);
            exportMaterialParams.Hash = exportMaterialParams.GetHashCode();
            exportParams.Add(exportMaterialParams);
        }
    }

    public static ExportMaterial CreateExportMaterial(UMaterialInterface material, int materialIndex = 0)
    {
        return CreateExportMaterial<ExportMaterial>(material, materialIndex);
    }

    public static T CreateExportMaterial<T>(UMaterialInterface material, int materialIndex = 0) where T : ExportMaterial, new()
    {
        var exportMaterial = new T
        {
            MaterialPath = material.GetPathName(),
            MaterialName = material.Name,
            SlotIndex = materialIndex
        };

        if (material is UMaterialInstanceConstant materialInstance)
        {
            var (textures, scalars, vectors, switches, componentMasks) = MaterialParameters(materialInstance);
            exportMaterial.Textures = textures;
            exportMaterial.Scalars = scalars;
            exportMaterial.Vectors = vectors;
            exportMaterial.Switches = switches;
            exportMaterial.ComponentMasks = componentMasks;
            exportMaterial.IsGlass = IsGlassMaterial(materialInstance);
            exportMaterial.MasterMaterialName = materialInstance.GetLastParent()?.Name;
        }
        else if (material is { } materialInterface)
        {
            var (textures, scalars, vectors) = MaterialParameters(materialInterface);
            exportMaterial.Textures = textures;
            exportMaterial.Scalars = scalars;
            exportMaterial.Vectors = vectors;
            exportMaterial.IsGlass = IsGlassMaterial(materialInterface);
        }

        exportMaterial.Hash = material.GetPathName().GetHashCode();
        return exportMaterial;
    }

    private static readonly string[] GlassMaterialNames = {
        "M_MED_Glass_Master",
        "M_MED_Glass_WithDiffuse",
        "M_Valet_Glass_Master",
        "M_MineralPowder_Glass",
        "M_CP_GlassGallery_Master",
        "M_LauchTheBalloon_Microwave_Glass",
        "M_MED_Glass_HighTower",
        "M_OctopusBall",
        "F_MED_SharpFang_Backpack_Glass_Master",
        "M_LlamaEye"
    };
    public static bool IsGlassMaterial(UMaterialInstanceConstant? materialInstance)
    {
        if (materialInstance is null) return false;

        var lastParent = materialInstance.GetLastParent();
        if (lastParent is null) return false;

        return GlassMaterialNames.Contains(lastParent.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsGlassMaterial(UMaterialInterface material)
    {
        if (material is UMaterialInstanceConstant materialInstance)
        {
            return IsGlassMaterial(materialInstance);
        }

        return GlassMaterialNames.Contains(material.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static UMaterialInterface? GetLastParent(this UMaterialInstanceConstant obj)
    {
        var hasParent = true;
        var activeParent = obj.Parent;
        while (hasParent)
        {
            if (activeParent is UMaterialInstanceConstant materialInstance)
            {
                activeParent = materialInstance.Parent;
            }
            else
            {
                hasParent = false;
            }
        }

        return activeParent as UMaterialInterface;
    }

    public static ExportPart Skeleton(USkeleton skeleton)
    {
        var part = new ExportPart
        {
            Part = "MasterSkeleton",
            MeshPath = skeleton.GetPathName()
        };
        Save(skeleton);
        return part;
    }

    public static readonly List<Task> Tasks = new();

    private static readonly ExporterOptions ExportOptions = new()
    {
        Platform = ETexturePlatform.DesktopMobile,
        LodFormat = ELodFormat.AllLods,
        MeshFormat = EMeshFormat.ActorX,
        TextureFormat = ETextureFormat.Png,
        ExportMorphTargets = true,
        SocketFormat = ESocketFormat.Bone,
        MaterialFormat = EMaterialFormat.AllLayersNoRef,
        ExportMaterials = false
    };

    private static readonly ExporterOptions StaticMeshExportOptions = new()
    {
        Platform = ETexturePlatform.DesktopMobile,
        LodFormat = ELodFormat.AllLods,
        MeshFormat = EMeshFormat.ActorX,
        TextureFormat = ETextureFormat.Png,
        ExportMorphTargets = true,
        SocketFormat = ESocketFormat.None,
        MaterialFormat = EMaterialFormat.AllLayersNoRef, 
        ExportMaterials = false
    };

    private static bool AllLodsExist(UObject mesh)
    {
        switch (mesh)
        {
            case USkeletalMesh skeletalMesh:
                if (!skeletalMesh.TryConvert(out var convertedSkel)) return false;
                for (var i = 0; i < convertedSkel.LODs.Count; i++)
                {
                    if (!File.Exists(GetExportPath(mesh, "psk", "_LOD" + i)))
                        return false;
                }

                break;
            case UStaticMesh staticMesh:
                if (!staticMesh.TryConvert(out var convertedStatic)) return false;
                for (var i = 0; i < convertedStatic.LODs.Count; i++)
                {
                    if (!File.Exists(GetExportPath(mesh, "pskx", "_LOD" + i)))
                        return false;
                }

                break;
        }

        return true;
    }

    public static void Save(UObject? obj)
    {
        Save(obj, out _);
    }

    public static void Save(UObject? obj, out string savedFilePath)
    {
        savedFilePath = string.Empty;
        if (obj is null) return;
        var task = Task.Run(() =>
        {
            var path = string.Empty;
            try
            {
                switch (obj)
                {
                    case USkeletalMesh skeletalMesh:
                    {
                        path = GetExportPath(skeletalMesh, "psk", "_LOD0");
                        if (AllLodsExist(skeletalMesh)) break;

                        var exporter = new MeshExporter(skeletalMesh, ExportOptions);
                        exporter.TryWriteToDir(App.AssetsFolder, out var label, out var savedFilePath);
                        break;
                    }

                    case UStaticMesh staticMesh:
                    {
                        path = GetExportPath(staticMesh, "pskx", "_LOD0");
                        if (AllLodsExist(staticMesh)) break;

                        var exporter = new MeshExporter(staticMesh, StaticMeshExportOptions);
                        exporter.TryWriteToDir(App.AssetsFolder, out var label, out var savedFilePath);
                        break;
                    }

                    case UTexture2D texture:
                    {
                        var extension = AppSettings.Current.ImageType switch
                        {
                            EImageType.PNG => "png",
                            EImageType.TGA => "tga"
                        };
                        path = GetExportPath(obj, extension);

                        var shouldExport = ShouldExportTexture(path, texture.GetFirstMip());
                        if (!shouldExport) break;

                        switch (AppSettings.Current.ImageType)
                        {
                            case EImageType.PNG:
                                File.WriteAllBytes(path, texture.Decode().Encode(SKEncodedImageFormat.Png, 100).ToArray()); // skia just be faster for png soooo
                                break;
                            case EImageType.TGA:
                                texture.DecodeImageSharp()?.SaveAsTga(path);
                                break;
                        }

                        break;
                    }

                    case UAnimSequence animation:
                    {
                        path = GetExportPath(obj, "psa");
                        if (File.Exists(path)) break;

                        var exporter = new AnimExporter(animation, ExportOptions);
                        exporter.TryWriteToDir(App.AssetsFolder, out var label, out var savedFilePath);
                        break;
                    }

                    case USkeleton skeleton:
                    {
                        path = GetExportPath(obj, "psk");
                        if (File.Exists(path)) break;

                        var exporter = new MeshExporter(skeleton, ExportOptions);
                        exporter.TryWriteToDir(App.AssetsFolder, out var label, out var savedFilePath);
                        break;
                    }
                }

                Log.Information("Exporting {ExportType}: {FileName} to {Path}", obj.ExportType, obj.Name, path);
            }
            catch (Exception e)
            {
                Log.Warning("Failed to Export {ExportType}: {FileName}", obj.ExportType, obj.Name);
                Log.Warning(e.Message);
            }

            return path;
        });
        
        Tasks.Add(task);
        
        savedFilePath = task.GetAwaiter().GetResult();
    }

    private static bool ShouldExportTexture(string path, FTexture2DMipMap mip)
    {
        if (!File.Exists(path)) return true;

        try
        {
            using var imageStream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(imageStream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.Default);
            var height = decoder.Frames[0].PixelHeight;
            var width = decoder.Frames[0].PixelWidth;
            return mip.SizeX > width || mip.SizeY > height;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static void SaveSoundWave(USoundWave soundWave, out string audioFormat, out string path)
    {
        var task = Task.Run(() =>
        {
            try
            {
                soundWave.Decode(true, out var format, out var data);
                if (data is null)
                {
                    soundWave.Decode(false, out format, out data);
                }
                if (data is null) return (string.Empty, string.Empty);;

                var exportFormat = format;
                var exportData = data;
                switch (format.ToLower())
                {
                    case "adpcm":
                        exportData = MusicQueueItem.ConvertedDataVGMStream(data).ReadToEnd();
                        exportFormat = "wav";
                        break;
                    case "binka":
                        exportData = MusicQueueItem.ConvertedDataBinkadec(data).ReadToEnd();
                        exportFormat = "wav";
                        break;
                }

                var path = GetExportPath(soundWave, exportFormat);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(path.Replace('\\', '/').SubstringBeforeLast('/'));
                    File.WriteAllBytes(path, exportData.ToArray());

                    Log.Information("Exporting {ExportType}: {FileName} to {Path}", soundWave.ExportType, soundWave.Name, path);
                }

                return (path, exportFormat);
            }
            catch (IOException e)
            {
                Log.Warning("Failed to export {ExportType}: {FileName}", soundWave.ExportType, soundWave.Name);
                Log.Warning(e.Message);
            }

            return (string.Empty, string.Empty);
        });
        Tasks.Add(task);

        var (outPath, format) = task.GetAwaiter().GetResult();
        audioFormat = format;
        path = outPath;
    }

    public static void SaveAdditiveAnim(UAnimSequence baseSequence, UAnimSequence additiveSequence)
    {
        additiveSequence.RefPoseSeq = new ResolvedLoadedObject(baseSequence);

        var exporter = new AnimExporter(additiveSequence, ExportOptions);
        exporter.TryWriteToDir(App.AssetsFolder, out var label, out var savedFilePath);

        Log.Information("Exporting {ExportType}: {FileName}", additiveSequence.ExportType, additiveSequence.Name);
    }

    public static string GetExportPath(UObject obj, string ext, string extra = "")
    {
        var path = obj.Owner != null ? obj.Owner.Name : string.Empty;
        path = path.SubstringBeforeLast('.');
        if (path.StartsWith("/")) path = path[1..];

        var directory = Path.Combine(App.AssetsFolder.FullName, path);
        Directory.CreateDirectory(directory.SubstringBeforeLast("/"));

        var finalPath = directory + $"{extra}.{ext.ToLower()}";
        return finalPath;
    }

    private static Sound LoadSound(USoundNodeWavePlayer player, float timeOffset = 0)
    {
        var soundWave = player.SoundWave?.Load<USoundWave>();
        return new Sound(soundWave, timeOffset, player.GetOrDefault("bLooping", false));
    }

    private static Sound LoadSound(USoundWave soundWave, float timeOffset = 0)
    {
        return new Sound(soundWave, timeOffset, false);
    }


    public static List<Sound> HandleAudioTree(USoundNode node, float offset = 0f)
    {
        var sounds = new List<Sound>();
        switch (node)
        {
            case USoundNodeWavePlayer player:
            {
                sounds.Add(LoadSound(player, offset));
                break;
            }
            case USoundNodeDelay delay:
            {
                foreach (var nodeObject in delay.ChildNodes)
                {
                    sounds.AddRange(HandleAudioTree(nodeObject.Load<USoundNode>(), offset + delay.GetOrDefault("DelayMin", delay.GetOrDefault<float>("DelayMax"))));
                }

                break;
            }
            case USoundNodeRandom random:
            {
                var index = App.RandomGenerator.Next(0, random.ChildNodes.Length);
                sounds.AddRange(HandleAudioTree(random.ChildNodes[index].Load<USoundNode>(), offset));
                break;
            }

            case UFortSoundNodeLicensedContentSwitcher switcher:
            {
                sounds.AddRange(HandleAudioTree(switcher.ChildNodes.Last().Load<USoundNode>(), offset));
                break;
            }
            case USoundNodeDialoguePlayer dialoguePlayer:
            {
                var dialogueWaveParameter = dialoguePlayer.Get<FStructFallback>("DialogueWaveParameter");
                var dialogueWave = dialogueWaveParameter.Get<UDialogueWave>("DialogueWave");
                var contextMappings = dialogueWave.Get<FStructFallback[]>("ContextMappings");
                var soundWave = contextMappings.First().Get<USoundWave>("SoundWave");
                sounds.Add(LoadSound(soundWave));
                break;
            }
            case USoundNode generic:
            {
                foreach (var nodeObject in generic.ChildNodes)
                {
                    sounds.AddRange(HandleAudioTree(nodeObject.Load<USoundNode>(), offset));
                }

                break;
            }
        }

        return sounds;
    }
}