using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace OpenGLGen
{
    class Program
    {
        static void Main(string[] args)
        {
            string glFile = "..\\..\\..\\..\\..\\KhronosRegistry\\gl.xml";

            // Generate OpenGL bindings
            DirectoryInfo workingDirectory = new DirectoryInfo("..\\..\\..\\..\\..\\OpenGL-Beef\\OpenGL\\src\\Generated");
            var api = new[] { "gl" };
            string namespaceText = "namespace OpenGL";
            string nativeClassText = "GL";
            GenerateBindings(glFile, workingDirectory, api, namespaceText, nativeClassText);
        }

        private static void GenerateBindings(string glFile, DirectoryInfo workingDirectory, string[] api, string namespaceText, string nativeClassText)
        {
            var spec = GLParser.FromFile(glFile, api);

            // Select version
            var version = spec.Versions[spec.Versions.Count - 1];

            // Write Enums
            using (var writer = new StreamWriter((Path.Combine(workingDirectory.FullName, "Enums.bf"))))
            {
                writer.WriteLine("using System;\n");
                writer.WriteLine($"{namespaceText};");

                int count = 0;
                foreach (var groupElem in version.Groups)
                {
                    // Separate one line betweens enums
                    if (count++ > 0)
                    {
                        writer.WriteLine();
                    }

                    writer.WriteLine($"[AllowDuplicates]");
                    writer.WriteLine($"public enum {groupElem.Name} : uint32");
                    writer.WriteLine("{");
                    foreach (var enumElem in groupElem.Enums)
                    {
                        if (IsUint(enumElem.Value))
                        {
                            writer.WriteLine($"\t{enumElem.ShortName} = {enumElem.Value},");
                        }
                    }
                    writer.WriteLine("}");
                }
            }

            // Write Commands
            using (var writer = new StreamWriter((Path.Combine(workingDirectory.FullName, $"{nativeClassText}.bf"))))
            {
                writer.WriteLine("using System;");
                writer.WriteLine($"{namespaceText};");
                writer.WriteLine($"extension {nativeClassText}");
                writer.WriteLine("{");
                writer.WriteLine("\tprivate static function void*(StringView) s_getProcAddress;\n");
                writer.WriteLine("\tprivate const CallingConventionAttribute.Kind CallConv = .Stdcall;");

                // Prototypes
                foreach (var command in version.Commands)
                {
                    writer.WriteLine();

                    // Delegate
                    StringBuilder delegateCommand = new StringBuilder($"\tprivate typealias {command.Name}_t = function ");
                    BuildReturnType(version, command, delegateCommand);
                    delegateCommand.Append($"(");
                    BuildParameterList(version, command, delegateCommand);
                    delegateCommand.Append(");");
                    writer.WriteLine(delegateCommand.ToString());

                    // internal function
                    writer.WriteLine($"\tprivate static {command.Name}_t p_{command.Name};");


                    writer.WriteLine("\t[CallingConvention(GL.CallConv)]");
                    // public function
                    StringBuilder function = new StringBuilder($"\tpublic static ");
                    BuildReturnType(version, command, function);
                    function.Append($" {command.Name}(");
                    BuildParameterList(version, command, function);
                    function.Append($") => p_{command.Name}(");
                    BuildParameterNamesList(command, function);
                    function.Append(");");
                    writer.WriteLine(function.ToString());
                }

                // Helper functions
                writer.WriteLine("\n\tpublic static void LoadGetString(function void*(StringView) getProcAddress)");
                writer.WriteLine("\t{");
                writer.WriteLine("\t\ts_getProcAddress = getProcAddress;");
                writer.WriteLine("\t\tLoadFunction(\"glGetString\", out p_glGetString);");
                writer.WriteLine("\t}");

                writer.WriteLine("\n\tpublic static void LoadAllFunctions(function void*(StringView) getProcAddress)");
                writer.WriteLine("\t{");
                writer.WriteLine("\t\ts_getProcAddress = getProcAddress;\n");

                foreach (var command in version.Commands)
                {
                    writer.WriteLine($"\t\tLoadFunction(\"{command.Name}\", out p_{command.Name});");
                }
                writer.WriteLine("\t}\n");

                writer.WriteLine("\tprivate static void LoadFunction<T>(StringView name, out T field)");
                writer.WriteLine("\t{");
                writer.WriteLine("\t\tvoid* funcPtr = s_getProcAddress(name);");
                writer.WriteLine("\t\tif (funcPtr != null)");
                writer.WriteLine("\t\t{");
                writer.WriteLine("\t\t\tfield = *(T*)(void*)&funcPtr;");
                writer.WriteLine("\t\t}");
                writer.WriteLine("\t\telse");
                writer.WriteLine("\t\t{");
                writer.WriteLine("\t\t\tfield = default(T);");
                writer.WriteLine("\t\t}");
                writer.WriteLine("\t}");

                writer.WriteLine("}");
            }
        }

        private static bool IsUint(string value)
        {
            bool isHex = false;

            if (value.StartsWith("0x"))
            {
                isHex = true;
                value = value.Substring(2);

                if (value.Length > 8)
                {
                    return false;
                }
            }

            uint result;
            if (isHex)
            {
                return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
            }
            else
            {
                return uint.TryParse(value, out result);
            }
        }

        private static void BuildReturnType(GLParser.GLVersion version, GLParser.GLCommand c, StringBuilder builder)
        {
            if (c.ReturnType.Type == "GLenum")
            {
                bool groupExists = version.Groups.Exists(g => g.Name == c.ReturnType.Group);

                var groupName = c.ReturnType.Group;

                // For GLenums that don't appear in the gl.xml file.
                if (!groupExists)
                {
                    groupName = "uint32";
                }

                builder.Append($"{groupName}");
            }
            else
            {
                builder.Append($"{ConvertGLType(c.ReturnType.Type)}");
            }
        }

        private static void BuildParameterList(GLParser.GLVersion version, GLParser.GLCommand c, StringBuilder builder)
        {
            if (c.Parameters.Count > 0)
            {
                foreach (var p in c.Parameters)
                {
                    var name = p.Name;

                    // Add @ to start of any names that are C# keywords to avoid conflict
                    if (name == "params" || name == "string" || name == "ref" || name == "base" || name == "box")
                    {
                        name = "@" + name;
                    }

                    if (p.Type == "GLenum")
                    {
                        bool groupExists = version.Groups.Exists(g => g.Name == p.Group);

                        var groupName = p.Group;

                        // For GLenums that don't appear in the gl.xml file.
                        if (!groupExists)
                        {
                            groupName = "uint32";
                        }

                        builder.Append($"{groupName} {name}, ");
                    }
                    else
                    {
                        builder.Append($"{ConvertGLType(p.Type)} {name}, ");
                    }
                }
                builder.Length -= 2;
            }
        }

        private static void BuildParameterNamesList(GLParser.GLCommand c, StringBuilder builder)
        {
            if (c.Parameters.Count > 0)
            {
                foreach (var p in c.Parameters)
                {
                    var name = p.Name;

                    // Add @ to start of any names that are C# keywords to avoid conflict
                    if (name == "params" || name == "string" || name == "ref" || name == "base" || name == "box")
                    {
                        name = "@" + name;
                    }

                    builder.Append($"{name}, ");
                }
                builder.Length -= 2;
            }
        }

        private static string ConvertGLType(string type)
        {
            switch (type)
            {
                case "GLboolean":
                    return "bool";

                case "GLenum":
                case "GLuint":
                case "GLbitfield":
                case "GLhandleARB":
                    return "uint32";

                case "GLint":
                case "GLsizei":
                case "GLsizeiptr":
                case "GLfixed":
                case "GLclampx":
                case "GLintptrARB":
                case "GLsizeiptrARB":
                    return "int32";

                case "GLuint *":
                case "const GLuint *":
                case "GLenum *":
                case "const GLenum *":
                    return "uint32*";

                case "GLdouble *":
                case "const GLdouble *":
                    return "double*";

                case "GLfloat *":
                case "const GLfloat *":
                    return "float*";

                case "GLint *":
                case "const GLint *":
                case "GLsizei *":
                case "const GLsizei *":
                case "GLsizeiptr *":
                case "const GLsizeiptr *":
                    return "int32*";

                case "GLushort *":
                case "const GLushort *":
                case "GLshort *":
                case "const GLshort *":
                    return "int16*";

                case "GLboolean *":
                case "const GLboolean *":
                    return "bool*";

                case "GLchar *":
                case "const GLchar *":
                    return "char8*";

                case "GLint64 *":
                case "const GLint64 *":
                    return "int64*";

                case "GLuint64 *":
                case "const GLuint64 *":
                    return "uint64*";

                case "GLubyte *":
                case "const GLubyte *":
                case "GLbyte *":
                case "const GLbyte *":
                    return "uint8*";

                case "void *":
                case "const void *":
                    return "void*";

                case "void **":
                case "const void **":
                    return "void**";

                case "GLfloat":
                case "GLclampf":
                    return "float";

                case "GLclampd":
                case "GLdouble":
                    return "double";

                case "GLubyte":
                    return "uint8";

                case "GLbyte":
                    return "int8";

                case "GLhalfNV": 
                case "GLushort":
                    return "uint16";

                case "GLshort":
                    return "int16";

                case "GLint64":
                case "GLint64EXT":
                    return "int64";

                case "GLuint64":
                case "GLuint64EXT":
                    return "uint64";

                case "GLsync":
                case "GLintptr":
                case "GLDEBUGPROC":
                case "GLeglImageOES":
                case "GLvdpauSurfaceNV":
                case "GLVULKANPROCNV":
                case "GLeglClientBufferEXT":
                case "GLDEBUGPROCKHR":
                case "GLDEBUGPROCAMD":
                case "GLDEBUGPROCARB":
                    return "void*";
            }
    
            if (type.Contains("*"))
            {
                return "void*";
            }

            return type;
        }
    }
}
