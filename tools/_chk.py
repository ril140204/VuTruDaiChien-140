import sys, re
# Parse the C# interop source if present
import glob, os
cands = glob.glob(r"D:\game\VuTruDaiChien_140\BepInEx\interop\Assembly-CSharp.cs")
print("cs source:", cands)
