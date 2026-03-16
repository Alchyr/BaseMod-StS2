BaseLib is a Slay the Spire 2 mod intended as a base for other mods to use as a dependency.

CURRENTLY REQUIRES THE STEAM BETA BRANCH

Wiki: https://alchyr.github.io/BaseLib-Wiki/

Mod start guide: https://github.com/Alchyr/ModTemplate-StS2/blob/master/README.md

Adding dependency for an existing mod: `<PackageReference Include="Alchyr.Sts2.BaseLib" Version="*" />`

You will need to download the `.dll`, `.pck`, and `.json` from releases and put them in `Slay the Spire 2/mods`.

Placeholder:

If using locally, adjust filepaths at top of csproj.
Use resulting .dll as a dependency for your own mod.
