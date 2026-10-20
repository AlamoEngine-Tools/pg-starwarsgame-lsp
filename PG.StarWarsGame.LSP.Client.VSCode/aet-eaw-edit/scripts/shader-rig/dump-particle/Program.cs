// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using PG.StarWarsGame.LSP.Assets.Models;

// DumpParticle <file.alo> <out.json>: the particle DTO as the webview receives it. Every library
// the reader pulls in is resolved from the server's build output (recorded by the project file),
// so nothing is rebuilt or copied.
var serverBin = Assembly.GetExecutingAssembly()
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .First(a => a.Key == "ServerBin").Value!;
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var candidate = Path.Combine(serverBin, name.Name + ".dll");
    return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
};

return Run(args);

static int Run(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: DumpParticle <file.alo> <out.json>");
        return 2;
    }

    var system = AloParticleReader.Read(File.ReadAllBytes(args[0]));

    // Camel-cased names and enum NAMES, which is what the server's preview serializer puts on the
    // wire (see PreviewSerialization).
    var settings = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter() },
    };
    File.WriteAllText(args[1], JsonConvert.SerializeObject(system, settings));
    Console.WriteLine($"{system.Name}: {system.Emitters.Count} emitters -> {args[1]}");
    return 0;
}
