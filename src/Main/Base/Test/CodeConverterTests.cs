﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Daniel Grunwald" email="daniel@danielgrunwald.de"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.PrettyPrinter;
using ICSharpCode.NRefactory.Visitors;
using NUnit.Framework;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Dom.NRefactoryResolver;

namespace ICSharpCode.SharpDevelop.Tests
{
	/// <summary>
	/// Tests advanced code-conversion features that rely on the type system.
	/// </summary>
	[TestFixture]
	public class CodeConverterTests
	{
		#region TestProgram (converting code)
		ProjectContentRegistry projectContentRegistry = ParserService.DefaultProjectContentRegistry;
		
		void TestProgramCS2VB(string sourceCode, string expectedOutput)
		{
			TestProgram(SupportedLanguage.CSharp, sourceCode, expectedOutput);
		}
		
		void TestProgramVB2CS(string sourceCode, string expectedOutput)
		{
			TestProgram(SupportedLanguage.VBNet, sourceCode, expectedOutput);
		}
		
		void TestProgram(SupportedLanguage sourceLanguage, string sourceCode, string expectedOutput)
		{
			DefaultProjectContent pc = new DefaultProjectContent();
			pc.ReferencedContents.Add(projectContentRegistry.Mscorlib);
			if (sourceLanguage == SupportedLanguage.VBNet) {
				pc.DefaultImports = new DefaultUsing(pc);
				pc.DefaultImports.Usings.Add("System");
				pc.DefaultImports.Usings.Add("Microsoft.VisualBasic");
			}
			pc.Language = sourceLanguage == SupportedLanguage.CSharp ? LanguageProperties.CSharp : LanguageProperties.VBNet;
			HostCallback.GetCurrentProjectContent = delegate {
				return pc;
			};
			
			ICSharpCode.NRefactory.IParser parser = ParserFactory.CreateParser(sourceLanguage, new StringReader(sourceCode));
			parser.Parse();
			Assert.AreEqual("", parser.Errors.ErrorOutput);
			
			NRefactoryASTConvertVisitor visitor = new NRefactoryASTConvertVisitor(pc);
			visitor.VisitCompilationUnit(parser.CompilationUnit, null);
			visitor.Cu.FileName = sourceLanguage == SupportedLanguage.CSharp ? "a.cs" : "a.vb";
			foreach (IClass c in visitor.Cu.Classes) {
				pc.AddClassToNamespaceList(c);
			}
			
			ParseInformation parseInfo = new ParseInformation();
			parseInfo.ValidCompilationUnit = visitor.Cu;
			
			if (sourceLanguage == SupportedLanguage.CSharp) {
				CSharpToVBNetConvertVisitor convertVisitor = new CSharpToVBNetConvertVisitor(pc, parseInfo);
				parser.CompilationUnit.AcceptVisitor(convertVisitor, null);
			} else {
				VBNetToCSharpConvertVisitor convertVisitor = new VBNetToCSharpConvertVisitor(pc, parseInfo);
				parser.CompilationUnit.AcceptVisitor(convertVisitor, null);
			}
			
			IOutputAstVisitor outputVisitor = sourceLanguage == SupportedLanguage.CSharp ? (IOutputAstVisitor)new VBNetOutputVisitor() : new CSharpOutputVisitor();
			outputVisitor.Options.IndentationChar = ' ';
			outputVisitor.Options.IndentSize = 2;
			using (SpecialNodesInserter.Install(parser.Lexer.SpecialTracker.RetrieveSpecials(),
			                                    outputVisitor)) {
				outputVisitor.VisitCompilationUnit(parser.CompilationUnit, null);
			}
			Assert.AreEqual("", outputVisitor.Errors.ErrorOutput);
			Assert.AreEqual(expectedOutput.Replace("\r", ""), outputVisitor.Text.Trim().Replace("\r", ""));
		}
		#endregion
		
		#region TestMember/TestStatement/TestExpression
		string IndentAllLines(string code)
		{
			StringBuilder b = new StringBuilder();
			using (StringReader r = new StringReader(code)) {
				string line;
				while ((line = r.ReadLine()) != null) {
					b.Append("  ");
					b.AppendLine(line);
				}
			}
			return b.ToString();
		}
		
		void TestMemberCS2VB(string sourceCode, string expectedCode)
		{
			TestProgramCS2VB("using System;\n" +
			                 "class MyClassName {\n" +
			                 IndentAllLines(sourceCode) +
			                 "}",
			                 
			                 "Imports System\n" +
			                 "Class MyClassName\n" +
			                 IndentAllLines(expectedCode) +
			                 "End Class");
		}
		
		void TestMemberVB2CS(string sourceCode, string expectedCode)
		{
			TestProgramVB2CS("Class MyClassName\n" +
			                 IndentAllLines(sourceCode) +
			                 "End Class",
			                 
			                 "using System;\n" +
			                 "using Microsoft.VisualBasic;\n" +
			                 "class MyClassName\n{\n" +
			                 IndentAllLines(expectedCode) +
			                 "}");
		}
		
		void TestStatementsCS2VB(string sourceCode, string expectedCode)
		{
			TestMemberCS2VB("void T() {\n" +
			                IndentAllLines(sourceCode) +
			                "}",
			                
			                "Private Sub T()\n" +
			                IndentAllLines(expectedCode) +
			                "End Sub");
		}
		
		void TestStatementsVB2CS(string sourceCode, string expectedCode)
		{
			TestMemberVB2CS("Private Sub T()\n" +
			                IndentAllLines(sourceCode) +
			                "End Sub",
			                "private void T()\n{\n" +
			                IndentAllLines(expectedCode) +
			                "}");
		}
		#endregion
		
		[Test]
		public void RaiseEventCS2VB()
		{
			TestMemberCS2VB("public event EventHandler Click;" +
			                "void T() { if (Click != null) { Click(this, EventArgs.Empty); } }",
			                "Public Event Click As EventHandler\n" +
			                "Private Sub T()\n" +
			                "  RaiseEvent Click(Me, EventArgs.Empty)\n" +
			                "End Sub");
			
			TestMemberCS2VB("public event EventHandler Click;" +
			                "void T() { Click(this, EventArgs.Empty); }",
			                "Public Event Click As EventHandler\n" +
			                "Private Sub T()\n" +
			                "  RaiseEvent Click(Me, EventArgs.Empty)\n" +
			                "End Sub");
		}
		
		[Test]
		public void ReferenceEqualityAndValueEquality()
		{
			// Reference equality:
			TestStatementsCS2VB("object a = new object();\n" +
			                    "object b = new object();\n" +
			                    "if (a == b) {\n" +
			                    "}",
			                    
			                    "Dim a As New Object()\n" +
			                    "Dim b As New Object()\n" +
			                    "If a Is b Then\n" +
			                    "End If");
			
			// Value type equality:
			TestStatementsCS2VB("int a = 3;\n" +
			                    "int b = 4;\n" +
			                    "if (a == b) {\n" +
			                    "}",
			                    
			                    "Dim a As Integer = 3\n" +
			                    "Dim b As Integer = 4\n" +
			                    "If a = b Then\n" +
			                    "End If");
			
			// String equality:
			TestStatementsCS2VB("string a = \"3\";\n" +
			                    "string b = \"4\";\n" +
			                    "if (a == b) {\n" +
			                    "}",
			                    
			                    "Dim a As String = \"3\"\n" +
			                    "Dim b As String = \"4\"\n" +
			                    "If a = b Then\n" +
			                    "End If");
		}
		
		[Test]
		public void FixVBCasing()
		{
			TestStatementsVB2CS("Dim obj as iDisposable\n" +
			                    "Obj.dispose()",
			                    "IDisposable obj = null;\n" +
			                    "obj.Dispose();");
		}
		
		[Test]
		public void FixVBCasingAndAddMethodCallParenthesis()
		{
			TestStatementsVB2CS("Dim i as Integer = appdomain.getcurrentthreadid",
			                    "int i = AppDomain.GetCurrentThreadId();");
		}
		
		[Test]
		public void IndexerExpression()
		{
			TestStatementsVB2CS("Dim i(10) as Integer\n" +
			                    "Dim i2 As Integer = i(4)",
			                    "int[] i = new int[11];\n" +
			                    "int i2 = i[4];");
			TestStatementsVB2CS("Dim s as string = appdomain.currentdomain.GetAssemblies()(1).location",
			                    "string s = AppDomain.CurrentDomain.GetAssemblies()[1].Location;");
		}
		
		[Test]
		public void Redim()
		{
			TestStatementsVB2CS("Dim i(10) as Integer\n" +
			                    "Redim i(20)",
			                    "int[] i = new int[11];\n" +
			                    "i = new int[21];");
		}
		
		[Test]
		public void RedimPreserve()
		{
			TestStatementsVB2CS("Dim i(10) as Integer\n" +
			                    "Redim Preserve i(20)",
			                    "int[] i = new int[11];\n" +
			                    "Array.Resize(ref i, 21);");
		}
		
		[Test]
		public void RedimMultidimensional()
		{
			TestStatementsVB2CS("Dim MyArray(,) as Integer\n" +
			                    "ReDim MyArray(5, 5)",
			                    "int[,] MyArray = null;\n" +
			                    "MyArray = new int[6, 6];\n");
		}
		
		[Test]
		public void RedimMultidimensionalPreserve()
		{
			TestStatementsVB2CS("Dim MyArray(5, 5) as Integer\n" +
			                    "ReDim Preserve MyArray(10, 10)",
			                    "int[,] MyArray = new int[6, 6];\n" +
			                    "MyArray = (int[,])Microsoft.VisualBasic.CompilerServices.Utils.CopyArray(MyArray, new int[11, 11]);");
		}
	}
}
