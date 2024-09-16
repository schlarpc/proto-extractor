using extractor.src.util;
using protoextractor.IR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace protoextractor.compiler.proto_scheme
{
	/*  Protobuffer example file

	        Syntax MUST be first line of the file!
	        We do declare package names.

	        syntax = "proto3";
	        package ns.subns;

	        import "myproject/other_protos.proto";

	        enum EnumAllowingAlias {
	          option allow_alias = true;
	          UNKNOWN = 0;
	          STARTED = 1;
	          RUNNING = 1;
	        }


	        message SearchRequest {
	          required string query = 1;
	          optional int32 page_number = 2;
	          optional int32 result_per_page = 3;
	          enum Corpus {
	            UNIVERSAL = 0;
	            WEB = 1;
	            IMAGES = 2;
	            LOCAL = 3;
	            NEWS = 4;
	            PRODUCTS = 5;
	            VIDEO = 6;
	          }
	          optional Corpus corpus = 4 [default = UNIVERSAL];
	        }

	        Each namespace maps to ONE package!

	*/

	class Proto3Compiler : DefaultProtoCompiler
	{
		private static string _StartSpacer = "//----- Begin {0} -----";
		private static string _EndSpacer = "//----- End {0} -----";
		private static string _Spacer = "//------------------------------";

		// String used to reference the original name from the source library.
		private static string _Reference = "ref: {0}";

		// Mapping of all namespace objects to their location on disk.
		private Dictionary<IRNamespace, string> _NSLocationCache;

		public Proto3Compiler(IRProgram program) : base(program)
		{
		}

		public override void Compile()
		{
			Program.Log.OpenBlock("Proto3Compiler::Compile");
			Program.Log.Info("Writing proto files to folder `{0}`", _path);

			if (DumpMode == true)
			{
				// Dump the program into one file and return.
				Dump();

				Program.Log.CloseBlock();
				return;
			}

			// Process file names.
			// This already includes the proto extension!
			_NSLocationCache = ProtoHelper.NamespacesToFileNames(_program.Namespaces,
																 PackageStructured);

			// Create/Open files for writing.
			foreach (var irNS in _NSLocationCache.Keys)
			{
				// Get filename of current namespace.
				var nsFileName = _NSLocationCache[irNS];
				// Make sure directory structure exists, before creating/writing file.
				var folderStruct = Path.Combine(_path, Path.GetDirectoryName(nsFileName));
				Directory.CreateDirectory(folderStruct);

				// Resolve all imports.
				var references = ProtoHelper.ResolveNSReferences(irNS);

				// Construct file for writing.
				var constructedFileName = Path.Combine(_path, nsFileName);
				var fileStream = File.Create(constructedFileName);
				using (fileStream)
				{
					var textStream = new StreamWriter(fileStream);
					using (textStream)
					{
						// Print file header.
						WriteHeaderToFile(irNS, constructedFileName, textStream);
						// Print imports.
						WriteImports(references, textStream);
						//// Write all enums..
						//WriteEnumsToFile(irNS, textStream, "");
						//// Followed by all messages.
						//WriteClassesToFile(irNS, textStream, "");

						WriteEnumsAndClassesSortedByNameToFile(irNS, textStream, "");
					}
				}
			}

			// Finish up..
			Program.Log.CloseBlock();
		}

		private void Dump()
		{
			// Open the dumpfile for writing.
			var dumpFileName = Path.Combine(_path, _dumpFileName);
			var dumpFileStream = File.Create(dumpFileName);
			using (dumpFileStream)
			{
				// Construct a textwriter for easier printing.
				var textStream = new StreamWriter(dumpFileStream);
				using (textStream)
				{
					// Print file header.
					WriteHeaderToFile(null, dumpFileName, textStream);

					// Loop each namespace and write to the dump file.
					foreach (var ns in _program.Namespaces)
					{
						// Start with namespace name..
						textStream.WriteLine(_StartSpacer, ns.ShortName);
						textStream.WriteLine();

						// No imports

						// Write all public enums..
						WriteEnumsToFile(ns, textStream);
						// Write all classes..
						WriteClassesToFile(ns, textStream);

						// End with spacer
						textStream.WriteLine();
						textStream.WriteLine(_EndSpacer, ns.ShortName);
						textStream.WriteLine(_Spacer);
					}
				}
			}
		}

		private void WriteHeaderToFile(IRNamespace ns, string fileName, TextWriter w)
		{
			w.WriteLine("syntax = \"proto3\";");
			if (ns != null)
			{
				var nsPackage = ProtoHelper.ResolvePackageName(ns);
				w.WriteLine("package {0};", nsPackage);
				// Write all file scoped options
				WriteFileOptions(ns, fileName, w);
			}
			w.WriteLine();

			var firstComment = "Proto extractor compiled unit - https://github.com/HearthSim/proto-extractor";
			WriteComments(w, "", firstComment);

			w.WriteLine();
		}

		private void WriteImports(List<IRNamespace> referencedNamespaces, TextWriter w)
		{
			// Get filenames for the referenced namespaces, from cache.
			var nsFileNames = _NSLocationCache.Where(kv => referencedNamespaces.Contains(
														 kv.Key)).Select(kv => kv.Value);
			// Order filenames in ascending order.
			var orderedImports = nsFileNames.OrderBy(x => x);

			foreach (var import in orderedImports)
			{
				// import "myproject/other_protos.proto";
				// IMPORTANT: Forward slashes!
				var formattedImport = import.Replace(Path.DirectorySeparatorChar, '/');
				w.WriteLine("import \"{0}\";", formattedImport);
			}
			// End with additionall newline
			w.WriteLine();
		}

		private void WriteEnumsAndClassesSortedByNameToFile(IRNamespace ns, TextWriter w, string prefix = "")
		{
			IEnumerable<IRTypeNode> nodes = ns.Enums.Concat<IRTypeNode>(ns.Classes);

			IComparer<string> comparer = new extractor.src.util.StringComparer();
			foreach (var irNode in nodes.OrderBy(e => e.ShortName, comparer))
			{
				// Don't write private types.
				if (irNode.IsPrivate)
				{
					continue;
				}

				if (irNode is IREnum irEnum)
				{
					WriteEnum(irEnum, w, prefix);
				}
				else if (irNode is IRClass irClass)
				{
					WriteMessage(irClass, w, prefix);
				}
			}
		}

		private void WriteEnumsToFile(IRNamespace ns, TextWriter w, string prefix = "")
		{
			foreach (var irEnum in ns.Enums.OrderBy(e => e.ShortName))
			{
				// Don't write private types.
				if (irEnum.IsPrivate)
				{
					continue;
				}
				WriteEnum(irEnum, w, prefix);
			}
		}

		private void WriteEnum(IREnum e, TextWriter w, string prefix)
		{
			string reference = String.Format(_Reference, e.OriginalName);
			//WriteComments(w, prefix, reference);

			// Type names are kept in PascalCase!
			w.WriteLine("{0}enum {1} {{", prefix, e.ShortName);

			if (ProtoHelper.HasEnumAlias(e))
			{
				w.WriteLine("{0}option allow_alias = true;", prefix + "\t");
			}

			// Make a copy of all properties.
			var propList = e.Properties.ToList();
			// For enums, the default value is the first defined enum value, which must be 0.
			// Find or create that property with value 0
			IREnumProperty zeroProp;
			IEnumerable<IREnumProperty> zeroPropEnumeration = propList.Where(prop => prop.Value == 0);
			if (!zeroPropEnumeration.Any())
			{
				zeroProp = new IREnumProperty()
				{
					// Enum values are all shared within the same namespace, so they must be
					// unique within that namespace!
					Name = e.ShortName.ToUpper() + "_AUTO_INVALID",
					Value = 0,
				};
			}
			else
			{
				zeroProp = zeroPropEnumeration.First();
				// And remove the property from the collection.
				propList.Remove(zeroProp);
			}

			// Write the zero property first - AS REQUIRED PER PROTO3!
			w.WriteLine("{0}{1}_{2} = {3};", prefix + "\t", e.ShortName, zeroProp.Name, zeroProp.Value);

			// Write out the other properties of the enum next
			foreach (IREnumProperty prop in propList.OrderBy(prop => prop.Value))
			{
				// Enum property names are NOT converted to snake case!
				w.WriteLine("{0}{1}_{2} = {3};", prefix + "\t", e.ShortName, prop.Name, prop.Value);
			}

			// End enum.
			w.WriteLine("{0}}}", prefix);
			w.WriteLine();
		}

		private void WriteClassesToFile(IRNamespace ns, TextWriter w, string prefix = "")
		{
			foreach (var irClass in ns.Classes.OrderBy(c => c.ShortName))
			{
				// Don't write private types.
				if (irClass.IsPrivate)
				{
					continue;
				}
				WriteMessage(irClass, w, prefix);
			}
		}

		private void WriteMessage(IRClass c, TextWriter w, string prefix)
		{
			string reference = String.Format(_Reference, c.OriginalName);
			//WriteComments(w, prefix, reference);

			// Type names are kept in PascalCase!
			w.WriteLine("{0}message {1} {{", prefix, c.ShortName);

			var oneofProperties = GetOneofProperties(c);

			// Write all private types first!
			WritePrivateTypesToFile(c, w, prefix + "\t");


			var sortedProps = c.Properties.OrderBy(prop => prop.Options.PropertyOrder);
			var propsBefore = sortedProps.TakeWhile(x => x.Options.PropertyOrder < (oneofProperties.FirstOrDefault()?.Options.PropertyOrder ?? int.MaxValue)).ToArray();
			// Write all fields last!
			foreach (IRClassProperty prop in propsBefore)
			{
				WriteClassProperty(w, c, prop, prefix);
			}

			if (oneofProperties.Any())
			{
				WriteOneOfMessage(c, oneofProperties, w, prefix + "\t");
			}

			foreach (IRClassProperty prop in sortedProps.Skip(propsBefore.Length))
			{
				WriteClassProperty(w, c, prop, prefix);
			}

			// End message.
			w.WriteLine("{0}}}", prefix);
			w.WriteLine();
		}

		private void WriteClassProperty(TextWriter w, IRClass c, IRClassProperty prop, string prefix)
		{
			IRClassProperty.ILPropertyOptions opts = prop.Options;
			// Proto3 syntax has implicit default values!

			string label = ProtoHelper.FieldLabelToString(opts.Label, true);
			string type = ProtoHelper.TypeTostring(prop.Type, c, prop.ReferencedType);
			string tag = opts.PropertyOrder.ToString();

			// In proto3, the default for a repeated field is PACKED=TRUE.
			// Only if it's not packed.. we set it to false.
			string packed = "";
			if (opts.IsPacked == false && opts.Label == FieldLabel.REPEATED)
			{
				//packed = "[packed=false]";
			}

			//string propName = prop.Name.PascalToSnake();
			string propName = char.ToLower(prop.Name[0]) + prop.Name.Substring(1);
			if (packed.Length > 0)
			{
				tag = tag.SuffixAlign();
			}

			w.WriteLine("{0}{1}{2}{3} = {4}{5};", prefix + "\t",
						label.SuffixAlign(), type.SuffixAlign(), propName,
						tag, packed);
		}

		private List<IRClassProperty> GetOneofProperties(IRClass c)
		{
			var enumEnumeration = c.PrivateTypes.OfType<IREnum>();
			var classEnumeration = c.PrivateTypes.OfType<IRClass>();

			var oneOfEnum = enumEnumeration.SingleOrDefault(x => x.ShortName == "MessageOneofCase");

			var oneOfProps = new List<IRClassProperty>();
			if (oneOfEnum != null)
			{
				foreach (var prop in oneOfEnum.Properties)
				{
					var classProp = c.Properties.SingleOrDefault(x => x.Name == prop.Name);
					if (classProp != null)
					{
						c.Properties.Remove(classProp);
						oneOfProps.Add(classProp);
					}
				}

				c.PrivateTypes.Remove(oneOfEnum);
			}

			return oneOfProps;
		}

		private void WriteOneOfMessage(IRClass c, IEnumerable<IRClassProperty> props, TextWriter w, string prefix)
		{
			w.WriteLine("{0}oneof message {{", prefix);

			foreach (IRClassProperty prop in props.OrderBy(prop => prop.Options.PropertyOrder))
			{
				IRClassProperty.ILPropertyOptions opts = prop.Options;
				// Proto3 syntax has implicit default values!

				string label = ProtoHelper.FieldLabelToString(opts.Label, true);
				string type = ProtoHelper.TypeTostring(prop.Type, c, prop.ReferencedType);
				string tag = opts.PropertyOrder.ToString();

				// In proto3, the default for a repeated field is PACKED=TRUE.
				// Only if it's not packed.. we set it to false.
				string packed = "";
				if (opts.IsPacked == false && opts.Label == FieldLabel.REPEATED)
				{
					packed = "[packed=false]";
				}

				//string propName = prop.Name.PascalToSnake();
				string propName = char.ToLower(prop.Name[0]) + prop.Name.Substring(1);
				if (packed.Length > 0)
				{
					tag = tag.SuffixAlign();
				}

				w.WriteLine("{0}{1}{2}{3} = {4}{5};", prefix + "\t",
							label.SuffixAlign(), type.SuffixAlign(), propName,
							tag, packed);
			}

			// End message.
			w.WriteLine("{0}}}", prefix);
		}

		private void WritePrivateTypesToFile(IRClass cl, TextWriter w, string prefix)
		{
			// Select enums and classes seperately.
			var enumEnumeration = cl.PrivateTypes.OfType<IREnum>();
			var classEnumeration = cl.PrivateTypes.OfType<IRClass>();

			// Write out each private enum first..
			foreach (var privEnum in enumEnumeration.OrderBy(e => e.ShortName))
			{
				WriteEnum(privEnum, w, prefix);
			}

			// Then all private classes.
			foreach (var privClass in classEnumeration.OrderBy(c => c.ShortName))
			{
				// This recursively writes the private types of this class (if any).
				WriteMessage(privClass, w, prefix);
			}
		}
	}
}
