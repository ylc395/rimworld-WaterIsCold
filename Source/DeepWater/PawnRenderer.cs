using System;
using RimWorld;
using Verse;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace WaterIsCold
{
    [HarmonyPatch(typeof(PawnRenderer))]
    public class PawnRenderer_Patch
    {
        // Disable rendering of shadow for pawns wading in deep water.
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(DrawShadowInternal))]
        public static IEnumerable<CodeInstruction> DrawShadowInternal(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            for( int i = 0; i < codes.Count; ++i )
            {
                // Log.Message("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // if (pawn.Swimming || pawn.DrawNonHumanlikeSwimmingGraphic)
                // Change to:
                // if (pawn.Swimming || DrawShadowInternal_Hook(pawn) || pawn.DrawNonHumanlikeSwimmingGraphic)
                if( codes[ i ].opcode == OpCodes.Ldarg_0
                    && i + 3 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldfld && codes[ i +1 ].operand.ToString() == "Verse.Pawn pawn"
                    && codes[ i + 2 ].opcode == OpCodes.Callvirt && codes[ i + 2 ].operand.ToString() == "Boolean get_Swimming()"
                    && codes[ i + 3 ].opcode == OpCodes.Brtrue_S )
                {
                    codes.Insert( i + 4, codes[ i ].Clone()); // load 'this'
                    codes.Insert( i + 5, codes[ i + 1 ].Clone()); // load 'pawn'
                    codes.Insert( i + 6, new CodeInstruction( OpCodes.Call,
                        typeof(PawnRenderer_Patch).GetMethod(nameof(DrawShadowInternal_Hook))));
                    codes.Insert( i + 7, codes[ i + 3 ].Clone());
                    found = true;
                    break;
                }
            }
            if(!found)
                Log.Error( "WaterIsCold: Failed to patch PawnRenderer.DrawShadowInternal()");
            return codes;
        }

        public static bool DrawShadowInternal_Hook( Pawn pawn )
        {
            return RenderAsInDeepWater( pawn );
        }

        // Disable rendering of body for pawns wading in deep water.
        [HarmonyTranspiler]
        [HarmonyPatch(nameof(ParallelGetPreRenderResults))]
        public static IEnumerable<CodeInstruction> ParallelGetPreRenderResults(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            for( int i = 0; i < codes.Count; ++i )
            {
                // Log.Message("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // if (swimming)
                // {
                //     pawnRenderFlags = ...
                // Prepend:
                // if (!swimming)
                //     ParallelGetPreRenderResults_Hook(pawn, ref pawnRenderFlags);
                if( codes[ i ].opcode == OpCodes.Ldarg_0
                    && i + 6 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldfld && codes[ i + 1 ].operand.ToString() == "Verse.Pawn pawn"
                    && codes[ i + 2 ].opcode == OpCodes.Callvirt && codes[ i + 2 ].operand.ToString() == "Boolean get_Swimming()"
                    && codes[ i + 3 ].IsStloc()
                    && codes[ i + 4 ].IsLdloc()
                    && codes[ i + 5 ].opcode == OpCodes.Brfalse_S
                    && codes[ i + 6 ].opcode == OpCodes.Ldloc_1 )
                {
                    Label label = generator.DefineLabel();
                    codes.Insert( i + 4, codes[ i + 4 ].Clone()); // load 'swimming'
                    codes.Insert( i + 5, new CodeInstruction( OpCodes.Brtrue_S, label )); // if true, skip
                    codes.Insert( i + 6, codes[ i ].Clone()); // load 'this'
                    codes.Insert( i + 7, codes[ i + 1 ].Clone()); // load 'pawn'
                    codes.Insert( i + 8, new CodeInstruction( OpCodes.Ldloca_S, 1 )); // load ref 'pawnRenderFlags'
                    codes.Insert( i + 9, new CodeInstruction( OpCodes.Call,
                        typeof(PawnRenderer_Patch).GetMethod(nameof(ParallelGetPreRenderResults_Hook))));
                    codes[ i + 10 ].labels.Add( label ); // the original codes[ i + 4 ]
                    found = true;
                }
            }
            if(!found)
                Log.Error( "WaterIsCold: Failed to patch PawnRenderer.ParallelGetPreRenderResults()");
            return codes;
        }

        public static void ParallelGetPreRenderResults_Hook( Pawn pawn, ref PawnRenderFlags pawnRenderFlags )
        {
            if( !RenderAsInDeepWater( pawn ))
                return;
            pawnRenderFlags &= ~PawnRenderFlags.Clothes;
            pawnRenderFlags |= PawnRenderFlags.NoBody;
        }

        public static bool RenderAsInDeepWater( Pawn pawn )
        {
            if (ModSettings_WaterIsCold.deepWater && !pawn.Swimming && pawn.Map != null && pawn.Spawned)
            {
                TerrainDef terrain = pawn.Position.GetTerrain(pawn.Map);
                if (terrain != null && terrain.IsWater && !Utility.IsShallowWater(terrain))
                    return true;
            }
            return false;
        }
    }
}
