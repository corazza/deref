using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Diagnostics;

public class Ref
{
    public string file;
    public string name;
    public List<string> path;

    public Ref(string file, string name, List<string> path)
    {
        this.file = file;
        this.name = name;
        this.path = path;
    }

    public static Ref FromString(string value)
    {
        string[] splitResult = value.Split('#');
        string name = splitResult[0];
        string[] rest = splitResult[1].Split('/');
        return new Ref(name, rest[0], rest.Skip(1).ToList());
    }
}

public class DerefContext
{
    private bool rerun = false;
    private Dictionary<string, JsonNode> files = new Dictionary<string, JsonNode>(); // file -> JsonNode

    private static JsonNode LoadFile(string file)
    {
        string jsonString = File.ReadAllText(file);
        JsonNode recipe = JsonNode.Parse(jsonString)!;
        return recipe;
    }

    public JsonNode GetFile(string file) {
        if (files.ContainsKey(file))
        {
            return files[file];
        }
        else
        {
            JsonNode recipe = LoadFile(file);
            files[file] = recipe;
            rerun = true;
            return recipe;
        }
    }

    private JsonNode FetchPath(JsonNode obj, List<string> path)
    {
        foreach (var entry in path)
        {
            if (obj is JsonObject)
            {
                obj = obj[entry];
            } else if (obj is JsonArray) {
                int i = Int32.Parse(entry) - 1;
                obj = obj[i];
            }
            else
            {
                throw new Exception("node has to be either JsonObject or JsonArray here");
            }
        }

        return obj;
    }

    private JsonNode FetchRef(Ref x)
    {
        JsonNode recipe = GetFile(x.file);
        Queue<JsonNode> toVisit= new Queue<JsonNode>();
        toVisit.Enqueue(recipe);

        while (toVisit.Count != 0)
        {
            JsonNode node = toVisit.Dequeue();
            if (node is JsonObject)
            {
                foreach (var entry in node.AsObject())
                {
                    if (entry.Key == "ime" && entry.Value.ToString() == x.name)
                    {
                        return FetchPath(node, x.path);
                    } else
                    {
                        if (entry.Value is JsonObject || entry.Value is JsonArray)
                        {
                            toVisit.Enqueue(entry.Value);
                        }
                    }
                }
            } else if (node is JsonArray)
            {
                foreach (var entry in node.AsArray())
                {
                    if (entry is JsonObject || entry is JsonArray)
                    {
                        toVisit.Enqueue(entry);
                    }
                }
            } else
            {
                throw new Exception("node has to be either JsonObject or JsonArray here");
            }
        }

        return null;
    }

    private void Deref(JsonObject obj, Ref x)
    {
        rerun = true;
        var contents = FetchRef(x).Deserialize<JsonNode>();
        if (contents != null)
        {
            if (obj.Parent == null || contents is JsonObject)
            {
                obj.Remove("ref");
                foreach (var entry in contents.AsObject())
                {
                    var copy = entry.Value.Deserialize<JsonNode>();
                    obj.Add(entry.Key, copy);
                }
            }
            else if (obj.Parent is JsonObject)
            {
                string key = "";
                foreach (var entry in obj.Parent.AsObject())
                {
                    if (entry.Value == obj)
                    {
                        key = entry.Key;
                        break;
                    }
                }
                obj.Parent[key] = contents;
            } else if (obj.Parent is JsonArray)
            {
                for (int i = 0; i < obj.Parent.AsArray().Count; ++i)
                {
                    if (obj.Parent[i] == obj)
                    {
                        obj.Parent[i] = contents;
                        break;
                    }
                }
            } else
            {
                throw new Exception("obj.Parent should be either null, JsonObject, or JsonArray");
            }
        }
    }

    private void Process(JsonNode root)
    {
        Queue<JsonNode> toVisit = new Queue<JsonNode>();
        toVisit.Enqueue(root);

        while (toVisit.Count != 0)
        {
            JsonNode node = toVisit.Dequeue();
            
            if (node is JsonObject)
            {
                var keys = new List<string>();
                foreach (var entry in node.AsObject())
                {
                    keys.Add(entry.Key);
                }

                foreach (var key in keys)
                {
                    if (key == "ref")
                    {
                        Ref x = Ref.FromString(node[key].ToString());
                        Deref(node.AsObject(), x);
                    }
                    else
                    {
                        if (node[key] is JsonObject || node[key] is JsonArray)
                        {
                            toVisit.Enqueue(node[key]);
                        }
                    }
                }
            }
            else if (node is JsonArray)
            {
                foreach (var entry in node.AsArray())
                {
                    if (entry is JsonObject || entry is JsonArray)
                    {
                        toVisit.Enqueue(entry);
                    }
                }
            }
            else
            {
                throw new Exception("node has to be either JsonObject or JsonArray here");
            }
        }
    }

    private void DerefLoop()
    {
        var filenames = files.Keys.ToList();
        foreach (var filename in filenames)
        {
            Process(files[filename]);
        }
    }

    public void DerefMain(string file)
    {
        JsonNode recipe = GetFile(file);
        rerun = true;
        while (rerun)
        {
            rerun = false;
            DerefLoop();
        }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        DerefContext context = new DerefContext();
        string filename = args[0];
        context.DerefMain(filename);
        Console.WriteLine(context.GetFile(filename));
    }
}
