﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Diagnostics.Contracts;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BibleMarkdown;

partial class Program
{

	static void ImportFromBibleMarkdown(string mdpath, string srcpath)
	{
		srcpath = Path.Combine(srcpath, "bibmark");
		if (!Directory.Exists(srcpath)) return;
		var sources = Directory.EnumerateFiles(srcpath)
			.Where(file => file.EndsWith(".md"));
		if (sources.Any())
		{
			if (FromSource)
			{
				Imported = true;

				foreach (var source in sources)
				{
					var dest = Path.Combine(mdpath, Path.GetFileName(source));
					File.Copy(source, dest, true);
				}
			}
		}
	}

    static void ImportFromUSFM(string mdpath, string srcpath)
	{
		var sources = Directory.EnumerateFiles(srcpath)
			.Where(file => file.EndsWith(".usfm"));

		if (sources.Any())
		{

			//var mdtimes = Directory.EnumerateFiles(mdpath)
			//	.Select(file => File.GetLastWriteTimeUtc(file));
			//var sourcetimes = sources.Select(file => File.GetLastWriteTimeUtc(file));

			//var mdtime = DateTime.MinValue;
			//var sourcetime = DateTime.MinValue;

			//foreach (var time in mdtimes) mdtime = time > mdtime ? time : mdtime;
			//foreach (var time in sourcetimes) sourcetime = time > sourcetime ? time : sourcetime;

			if (FromSource)
			{
				Imported = true;

				int bookno = 1;
				string booknostr = "00.1";
				foreach (var source in sources)
				{
					var src = File.ReadAllText(source);

					src = PreprocessImportUSFM(src);

					var bookm = Regex.Matches(src, @"(\\h|\\toc1|\\toc2|\\toc3)\s+(.*?)$", RegexOptions.Multiline)
						.Select(m => m.Groups[2].Value.Trim())
						.OrderBy(name => name.Where(ch => ch == ' ').Count())
						.ThenBy(b => b.Count(ch => char.IsUpper(ch)))
						.ThenBy(b => b.Length)
						.ToArray();
					var book = bookm
						.FirstOrDefault();

					var namesfile = Path.Combine(mdpath, "src", "booknames.xml");
					var useNames = File.Exists(namesfile);

					XElement[] xmlbooks = new XElement[0];

					var books = Books[Language].Values.ToArray();

					if (useNames)
					{
						var book2 = books.FirstOrDefault(b => bookm.Any(bm => b.Name == bm));
						if (book2 != null)
						{
							book = book2.Name;
							bookno = book2.Number;
							booknostr = $"{bookno:d2}";
						}
						else
						{
							booknostr = $"00.{bookno:d2}";
						}
					}

					src = Regex.Match(src, @"\\c\s+[0-9]+.*", RegexOptions.Singleline).Value; // remove header that is not content of a chapter

					src = src.Replace("\r", "").Replace("\n", ""); // remove newlines

					src = Regex.Replace(src, @"(?<=\\c\s+[0-9]+\s*(\\s[0-9]+\s+[^\\]*?)?)\\p", ""); // remove empty paragraph after chapter

					src = src.Replace("[", "\\[").Replace("]", "\\]"); // escape [ and ]

					src = Regex.Replace(src, @"\\m?s(?<level>[0-9]+)\s*(?<text>[^\\$]+)", m => // section titles
					{
						int n = 1;
						int.TryParse(m.Groups["level"].Value, out n);
						n++;
						return $"{new String('#', n)} {m.Groups["text"].Value.Trim()}{Environment.NewLine}";
					}, RegexOptions.Singleline);

					src = Regex.Replace(src, @"\\sp\s+(.*?)(?=\s*\\(?!w))", $"\\{Environment.NewLine}*$1* \\{Environment.NewLine}", RegexOptions.Singleline); // speaker headings

					bool firstchapter = true;
					src = Regex.Replace(src, @"\\c\s+([0-9]+\s*)", m => // chapters
					{
						var res = firstchapter ? $"# {m.Groups[1].Value}{Environment.NewLine}" : $"{Environment.NewLine}{Environment.NewLine}# {m.Groups[1].Value}{Environment.NewLine}";
						firstchapter = false;
						return res;
					});

					if (Program.ParagraphVerses) src = Regex.Replace(src, @"\\v\s+([0-9]+)", "@$1"); // verse numbers
					else src = Regex.Replace(src, @"\\v\s+([0-9]+)", "^$1^"); // verse numbers

					// footnotes
					int n = 0;
					bool replaced;
					do
					{
						replaced = false;
						src = Regex.Replace(src, @"(?<=(?<dotbefore>[.:;?!¿¡])?)\\(?<type>[fx])\s*[+-?]\s*(?<footnote>.*?)\\\k<type>\*(?=(?<spaceafter>\s)?)(?<body>.*?(?=\s*#|\\p|$))", m =>
						{
							var space = n == 0 ? Environment.NewLine : " ";
							var spacebefore = m.Groups["dotbefore"].Success ? "" : "";
							var spaceafter = m.Groups["spaceafter"].Success ? "" : " ";
							replaced = true;
							var foottxt = m.Groups["footnote"].Value;
							foottxt = Regex.Replace(foottxt, @"([0-9]+)[:,]\s+([0-9]+)", "$1:$2", RegexOptions.Singleline);
							return $"{spacebefore}^{Label(n)}^{spaceafter}{m.Groups["body"].Value}{space}^{Label(n)}^[{foottxt}]";
						}, RegexOptions.Singleline);
						n++;
					} while (replaced);

					src = Regex.Replace(src, @"\\p[ \t]*", $"{Environment.NewLine}{Environment.NewLine}"); // replace new paragraph with empty line
					src = Regex.Replace(src, @"\|([a-zA-Z-]+=""[^""]*""\s*)+", ""); // remove word attributes
					src = Regex.Replace(src, @"\\em\s*(.*?)\s*\\em\*", "*$1*", RegexOptions.Singleline); // italics
					src = Regex.Replace(src, @"\\bd\s*(.*?)\s*\\bd\*", "**$1**", RegexOptions.Singleline); // bold
					src = Regex.Replace(src, @"\\it\s*(.*?)\s*\\it\*", "*$1*", RegexOptions.Singleline); //italics
					src = Regex.Replace(src, @"\\sc\s*(.*?)\s*\\sc\*", "[$1]{.smallcaps}", RegexOptions.Singleline); // smallcaps
					src = Regex.Replace(src, @"\\wj\s*(.*?)\s*\\wj\*", "[$1]{.wj}", RegexOptions.Singleline); // words of jesus
                    //src = Regex.Replace(src, @"\\nd\s*(.*?)\s*\\nd\*", "[$1]{.smallcaps}", RegexOptions.Singleline); // name of God
                    src = Regex.Replace(src, @"\\nd\s*(.*?)\s*\\nd\*", match =>
					{
						return match.Value.ToUpper();
					}, RegexOptions.Singleline); // name of God

                    src = Regex.Replace(src, @"\\\+?\w+(\*|[ \t]*)?", "", RegexOptions.Singleline); // remove usfm tags
					src = Regex.Replace(src, @" +", " "); // remove multiple spaces
					src = Regex.Replace(src, @"\^\[([0-9]+)[.,:]([0-9]+)", "^[**$1:$2**"); // bold verse references in footnotes
					src = Regex.Replace(src, @"((?<![0-9]+|\s|{)\.(?![0-9]+)|\?|!|;|(?<![0-9]+):(?![0-9]+)|(?<![0-9]+),(?![0-9]+))(\w|“|¿|¡)", "$1 $2"); // Add space after dot
					src = Regex.Replace(src, @"(?<!^|(?:^|\n)[ \t]*\r?\n|(?:^|\n)#+[ \t]+[^\n]*\n)#", $"{Environment.NewLine}#", RegexOptions.Singleline); // add blank line over title
					if (LowercaseFirstWords) // needed for ReinaValera1909, it has uppercase words on every beginning of a chapter
					{
						src = Regex.Replace(src, @"(\^1\^ \w)(\w*)", m => $"{m.Groups[1].Value}{m.Groups[2].Value.ToLower()}");
						src = Regex.Replace(src, @"(\^1\^ \w )(\w*)", m => $"{m.Groups[1].Value}{m.Groups[2].Value.ToLower()}");
					}

					src = Regex.Replace(src, @"\^[0-9]+\^(?=\s*(\^[0-9]+\^|#|$|\^[a-zA-Z]\^\[))", "", RegexOptions.Singleline); // remove empty verses
					src = Regex.Replace(src, @"@[0-9]+(?=\s*(@[0-9]+|#|$|\^[a-zA-Z]\^\[))", "", RegexOptions.Singleline); // remove empty verses
					src = Regex.Replace(src, @"(?<!\s|^)(\^[0-9]+\^)", " $1", RegexOptions.Singleline);
					src = Regex.Replace(src, @"(?<=(^|\n)#[ \t]+[0-9]+[ \t]*\r?\n)(##.*?\r?\n)?(?<verse0>.*?)(?=\s*\^1\^)", match => // set italics on verse 0
					{
						if (match.Value.Length > 0)
						{
							return $@"*{match.Value}* \{Environment.NewLine}";
						}
						else return match.Value;
					}, RegexOptions.Singleline);
					if (EachVerseOnNewLine)
					{
						src = Regex.Replace(src, @"(?<!^)(\^[0-9]+\^)", $"{Environment.NewLine}$1", RegexOptions.Multiline);
						src = Regex.Replace(src, @"(?<!^)(@[0-9]+)", $"{Environment.NewLine}$1", RegexOptions.Multiline);
					}

					var md = Path.Combine(mdpath, $"{booknostr}-{book}.md");
					bookno++;
					if (!string.IsNullOrWhiteSpace(src))
					{
						File.WriteAllText(md, src);
						LogFile(md);
					}
				}
			}
		}
	}

	public static void ImportFromBibleEdit(string srcpath)
	{
		var root = Path.Combine(srcpath, "bibleedit");
		if (FromSource && Directory.Exists(root))
		{
			Log("Import from BibleEdit");

			var oldfiles = Directory.EnumerateFiles(srcpath, "*.usfm");
			foreach (var of in oldfiles) File.Delete(of);

			var folders = Directory.EnumerateDirectories(root).ToArray();
			if (folders.Length == 1) folders = Directory.EnumerateDirectories(Path.Combine(folders[0])).ToArray();

			var namesfile = Path.Combine(srcpath, "booknames.xml");
			var books = Books[Language].Values;

			int fileno = 1;

			foreach (var folder in folders)
			{
				var chapters = Directory.EnumerateDirectories(folder).ToArray();
				int i = 0;
				chapters = chapters.OrderBy(f =>
				{
					var name = Path.GetFileName(f);
					int n;
					if (!int.TryParse(name, out n)) n = i;
					i++;
					return n;
				})
				.ToArray();

				var files = chapters
					.Select(ch => File.ReadAllText(Path.Combine(ch, "data")))
					.ToArray();

				var bookm = Regex.Matches(files[0], @"(\\h|\\toc1|\\toc2|\\toc3)\s+(.*?)$", RegexOptions.Multiline)
					.Select(m => m.Groups[2].Value.Trim())
					.ToArray();
				var book = books
					.Where(e => bookm.Any(b => string.Compare(e.Name, b, true) == 0))
					.FirstOrDefault();
				int index = fileno++;
				string bookname;
				if (book != null)
				{
					bookname = book.Name;
					index = book.Number;
				}
				else
				{
					bookname = bookm.FirstOrDefault();
				}

				var txt = new StringBuilder();
				foreach (string file in files)
				{
					txt.AppendLine(file);
				}

				var usfmfile = Path.Combine(srcpath, $"{index:d2}-{bookname}.usfm");
				File.WriteAllText(usfmfile, txt.ToString());
				LogFile(usfmfile);
			}
		}
	}
	public static void ImportFromTXT(string mdpath, string srcpath)
	{
		var sources = Directory.EnumerateFiles(srcpath)
			.Where(file => file.EndsWith(".txt"));

		if (sources.Any())
		{

			//var mdtimes = Directory.EnumerateFiles(mdpath)
			//	.Select(file => File.GetLastWriteTimeUtc(file));
			//var sourcetimes = sources.Select(file => File.GetLastWriteTimeUtc(file));

			//var mdtime = DateTime.MinValue;
			//var sourcetime = DateTime.MinValue;

			//foreach (var time in mdtimes) mdtime = time > mdtime ? time : mdtime;
			//foreach (var time in sourcetimes) sourcetime = time > sourcetime ? time : sourcetime;

			if (FromSource)
			{
				Imported = true;

				int bookno = 1;
				string book = null;
				string chapter = null;
				string md;

				var s = new StringBuilder();
				foreach (var source in sources)
				{
					var src = File.ReadAllText(source);
					var matches = Regex.Matches(src, @"(?:^|\n)(.*?)\s*([0-9]+):([0-9]+)(.*?)(?=$|\n[^\n]*?[0-9]+:[0-9]+)", RegexOptions.Singleline);

					foreach (Match m in matches)
					{
						var bk = m.Groups[1].Value;
						if (book != bk)
						{
							if (book != null)
							{
								md = Path.Combine(mdpath, $"{bookno++:D2}-{book}.md");
								File.WriteAllText(md, s.ToString());
								LogFile(md);
								s.Clear();
							}
							book = bk;
						}

						var chap = m.Groups[2].Value;
						if (chap != chapter)
						{
							chapter = chap;
							if (chapter != "1")
							{
								s.AppendLine();
								s.AppendLine();
							}
							s.AppendLine($"# {chapter}");
						}

						string verse = m.Groups[3].Value;
						string text = Regex.Replace(m.Groups[4].Value, @"\r?\n", " ").Trim();
						if (Program.ParagraphVerses)
						{
							s.Append($"{(verse == "1" ? "" : (EachVerseOnNewLine ? $"{Environment.NewLine}" : " "))}@{verse} {text}");
						}
						else
						{
							s.Append($"{(verse == "1" ? "" : (EachVerseOnNewLine ? $"{Environment.NewLine}" : " "))}^{verse}^ {text}");
						}
					}
				}
				md = Path.Combine(mdpath, $"{bookno++:D2}-{book}.md");
				File.WriteAllText(md, s.ToString());
				LogFile(md);
			}
		}
	}

	public static void ImportFromZefania(string mdpath, string srcpath)
	{
		var sources = Directory.EnumerateFiles(srcpath)
			.Where(file => file.EndsWith(".xml"));

		//var mdtimes = Directory.EnumerateFiles(mdpath)
		//	.Select(file => File.GetLastWriteTimeUtc(file));
		//var sourcetimes = sources.Select(file => File.GetLastWriteTimeUtc(file));

		//var mdtime = DateTime.MinValue;
		//var sourcetime = DateTime.MinValue;

		//foreach (var time in mdtimes) mdtime = time > mdtime ? time : mdtime;
		//foreach (var time in sourcetimes) sourcetime = time > sourcetime ? time : sourcetime;

		if (FromSource)
		{

			foreach (var source in sources)
			{
				using (var stream = File.Open(source, FileMode.Open, FileAccess.Read))
				{
					var root = XElement.Load(stream);

					if (root.Name != "XMLBIBLE") continue;

					Parallel.ForEach(root.Elements("BIBLEBOOK"), book =>
					{
						Imported = true;

						StringBuilder text = new StringBuilder();
						var file = $"{((int)book.Attribute("bnumber")):D2}-{(string)book.Attribute("bname")}.md";
						var firstchapter = true;

						foreach (var chapter in book.Elements("CHAPTER"))
						{
							if (!firstchapter)
							{
								text.AppendLine(""); text.AppendLine();
							}
							firstchapter = false;
							text.Append($"# {((int)chapter.Attribute("cnumber"))}{Environment.NewLine}");
							var firstverse = true;

							foreach (var verse in chapter.Elements("VERS"))
							{
								if (!firstverse)
								{
									if (EachVerseOnNewLine) text.AppendLine();
									else text.Append(" ");
								}
								firstverse = false;
								if (Program.ParagraphVerses) text.Append($"@{((int)verse.Attribute("vnumber"))} ");
								else text.Append($"^{((int)verse.Attribute("vnumber"))}^ ");
								text.Append(verse.Value);
							}
						}

						var md = Path.Combine(mdpath, file);
						File.WriteAllText(md, text.ToString());
						LogFile(md);
					});
				}
			}
		}
	}

	public static void ImportFromXmlOther(string mdpath, string srcpath)
	{
		var sources = Directory.EnumerateFiles(srcpath)
			.Where(file => file.EndsWith(".xml"));

		//var mdtimes = Directory.EnumerateFiles(mdpath)
		//	.Select(file => File.GetLastWriteTimeUtc(file));
		//var sourcetimes = sources.Select(file => File.GetLastWriteTimeUtc(file));

		//var mdtime = DateTime.MinValue;
		//var sourcetime = DateTime.MinValue;

		//foreach (var time in mdtimes) mdtime = time > mdtime ? time : mdtime;
		//foreach (var time in sourcetimes) sourcetime = time > sourcetime ? time : sourcetime;

		if (FromSource)
		{

			foreach (var source in sources)
			{
				using (var stream = File.Open(source, FileMode.Open, FileAccess.Read))
				{
					var root = XElement.Load(stream);

					if (root.Name != "bible") continue;

					int booknumber = 1;
					foreach (var book in root.Elements("b"))
					{
						Imported = true;

						StringBuilder text = new StringBuilder();
						var file = $"{booknumber:D2}-{(string)book.Attribute("n")}.md";
						var firstchapter = true;

						foreach (var chapter in book.Elements("c"))
						{
							if (!firstchapter)
							{
								text.AppendLine(""); text.AppendLine();
							}
							firstchapter = false;
							text.Append($"# {((int)chapter.Attribute("n"))}{Environment.NewLine}");
							var firstverse = true;

							foreach (var verse in chapter.Elements("v"))
							{
								if (!firstverse)
								{
									if (EachVerseOnNewLine) text.AppendLine();
									else text.Append(" ");
								}
								firstverse = false;
								if (Program.ParagraphVerses) text.Append($"@{((int)verse.Attribute("n"))} ");
								else text.Append($"^{((int)verse.Attribute("n"))}^ ");
								text.Append(verse.Value);
							}
						}

						var md = Path.Combine(mdpath, file);
						File.WriteAllText(md, text.ToString());
						LogFile(md);

						booknumber++;
					}
				}
			}
		}
	}

	public static Outline ReadOutlines(string path)
	{
		Outline items = new Outline();

        var files = Directory.EnumerateFiles(path, "*.md")
			.Where(file => file.EndsWith("outline.md", StringComparison.OrdinalIgnoreCase))
			.Concat(Directory.EnumerateFiles(path, "*.xml")
            .Where(file => file.EndsWith("outline.xml", StringComparison.OrdinalIgnoreCase)))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var file in files.ToList())
		{
			if (file.EndsWith(".xml") && files.Contains(Path.ChangeExtension(file, ".md"))) files.Remove(file);
        }

        foreach (var file in files)
		{
			if (file.EndsWith(".md")) items.AddMdOutline(file);
			else if (file.EndsWith(".xml")) items.AddXmlOutline(file);
        }

		items.Sort();

		// remove ParagraphItems that are duplicate of a TitleItem and remove duplicate BookItems and ChapterItems
		var temp = items;
		items = new Outline();
		items.Append = temp.Append;
		List<OutlineItem> sameLocation = new List<OutlineItem>(10);
		for (int i = 0; i < temp.Count;)
		{
			int j = i + 1;
			sameLocation.Add(temp[i]);
			while (j < temp.Count && Location.Compare(temp[i].Location, temp[j].Location) == 0)
			{
				sameLocation.Add(temp[j++]);
			}
			i = j;
			IEnumerable<OutlineItem> toAdd;
			if (sameLocation.Any(x => x is TitleItem)) toAdd = sameLocation.Where(x => !(x is ParagraphItem));
			else toAdd = sameLocation;
			if (toAdd.OfType<BookItem>().Count() > 1) toAdd = toAdd
					.Where(x => !(x is BookItem))
					.Concat(
						toAdd.OfType<BookItem>()
							.Take(1)
					);
			if (toAdd.OfType<ChapterItem>().Count() > 1) toAdd = toAdd
					.Where(x => !(x is ChapterItem))
					.Concat(
						toAdd.OfType<ChapterItem>()
							.Take(1)
					);
			items.AddRange(toAdd);
			sameLocation.Clear();
		}

		BookItem? bookItem = null;
		// set BookItem items collection
		foreach (var item in items)
		{
			if (item is BookItem)
			{
				bookItem = (BookItem)item;
				bookItem.Items.Clear();
			}
			else if (bookItem != null && item.Location.Book.Number == bookItem.Location.Book.Number) bookItem.Items.Add(item);
		}

		return items;
	}

	public static void ImportParallelVerses(string path)
	{
		Outline items = new Outline();

		// import parallel verses
		int lastbook = -1, lastchapter = -11;
        foreach (var parverse in new BibleMarkdown.ParallelVerses().Load(Path.Combine(path, "src")))
		{
            StringBuilder footnote = new StringBuilder($"^[**{parverse.Verse.Chapter}:{(parverse.Verse.Verse > 1 ? parverse.Verse.Verse : 1)}** ");
			bool firstpv = true;
			foreach (var pv in parverse.ParallelVerses)
			{
				if (firstpv) firstpv = false;
				else footnote.Append("; ");
				if (pv.Verse <= 0) pv.Verse = 1;
				footnote.Append($"{pv.Book.Abbreviation} {pv.Chapter},{pv.Verse}");
				if (pv.UpToVerse.HasValue && pv.UpToVerse.Value > 0) footnote.Append($"-{pv.UpToVerse}");
			}
			footnote.Append("]");
			if (lastbook != parverse.Verse.Book.Number)
			{
				var file = $"{parverse.Verse.Book.Number:d2}-{parverse.Verse.Book.Name}.md";
				items.Add(new BookItem(parverse.Verse.Book, file));
				lastchapter = -1;
			}
			if (lastchapter != parverse.Verse.Chapter)
			{
				items.Add(new ChapterItem(parverse.Verse.Book, parverse.Verse.Chapter));
			}
			items.Add(new FootnoteItem(parverse.Verse.Book, footnote.ToString(), parverse.Verse.Chapter, parverse.Verse.Verse));
		}

		items.Append = true;
		if (items.Count > 0) items.Save(Path.Combine(path, "ParallelVerses.outline.md"));
	}

	public static string ApplyOutline(string src, string file, Outline? items)
	{
		if (items == null || items.Count == 0) return src;

        var srcname = Path.GetFileName(file);
		string bookname = Books.Name(srcname);

		var bookItem = items
			.OfType<BookItem>()
			.FirstOrDefault(b => b.File == srcname);
		if (bookItem == null)
		{
			Log($"Book for {Regex.Replace(srcname, @"(^[0-9]{2}(\.[0-9]+)?-)|(.md$)", "")} not found in outline.md");
			return src;
			//continue; // use when loop with foreach for sequential loop for debugging
		}
		// remove current outline

		bool replaced = true;
		if (!items.Append && items.OfType<FootnoteItem>().Any())
		{
			// remove bibmark footnotes.
			while (replaced)
			{
				replaced = false;
				src = Regex.Replace(src, @"\^(?<mark>[a-zA-Z]+)\^(?!\[)(?<text>.*?)(?:\^\k<mark>(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]))[ \t]*\r?\n?", m =>
				{
					replaced = true;
					return $"{m.Groups["footnote"].Value}{m.Groups["text"].Value}";
				}, RegexOptions.Singleline);
			}
		}
		if (!items.Append && items.OfType<ParagraphItem>().Any())
		{
			src = Regex.Replace(src, @"(\r?\n|^)[ \t]*\r?\n(?![ \t]*#)", "", RegexOptions.Singleline); // remove blank line
		}
		//src = Regex.Replace(src, @"(?<!(^|\n)#.*?)\r?\n(?![ \t]*#)", " ", RegexOptions.Singleline);
		src = Regex.Replace(src, "  +", " "); // remove multiple spaces.


		if (!items.Append && items.OfType<TitleItem>().Any())
		{
			src = Regex.Replace(src, @"(?<=^|\n)##+.*?\r?\n", "", RegexOptions.Singleline); // remove titles
		}
		// src = Regex.Replace(src, @"(\s*\^[a-zA-Z]+\^)|(([ \t]*\^[a-zA-Z]+\^\[[^\]]*\])+([ \t]*\r?\n)?)", "", RegexOptions.Singleline); // remove footnotes
		src = Regex.Replace(src, @"//!verse-paragraphs.*?(\r?\n|$)", "", RegexOptions.Singleline); // remove verse paragraphs

		if (bookItem.VerseParagraphs) src = $"//!verse-paragraphs{Environment.NewLine}{src}";

		var frames = bookItem.Items.GetEnumerator();
		var book = Books["default", bookname];
		OutlineItem? frame = frames.MoveNext() ? frames.Current : null;
		int chapter = 0;
		int verse = -1;

		src = Regex.Replace(src, @"(?<=^|\n)#[ \t]+(?<chapter>[0-9]+)(\s*\r?\n|$)|\^(?<verse>[0-9]+)\^.*?(?=\s*\^[0-9]+\^|\s*@[0-9]+|\s*#|\s*$)|@(?<verse2>[0-9]+).*?(?=\s*\^[0-9]+\^|\s*@[0-9]+|\s*#|\s*$)|(?<=(^|\n)#[ \t]+[0-9]+[ \t]*\r?\n(##[ \t]+.*?\r?\n)?)(?<empty>.*?)(?=\s*\\?\s*\^1\^|\s*\\?\s*@1|\s*#|\s*$)", m =>
		// empty is special text at the beginning of a psalm without verse number
		{
			var txt = m.Value;

			if (m.Groups["chapter"].Success) // chapter
			{
				int.TryParse(m.Groups["chapter"].Value, out chapter); verse = -1;
			}
			else if (m.Groups["verse"].Success) // verse
			{
				int.TryParse(m.Groups["verse"].Value, out verse);
			}
			else if (m.Groups["verse2"].Success) // verse
			{
				int.TryParse(m.Groups["verse2"].Value, out verse);
			}
			else if (m.Groups["empty"].Success) //introduction line of a psalm
			{
				verse = 0;
			}

			while (frame != null && frame.Chapter <= chapter && frame.Verse <= verse)
			{

				if (frame is TitleItem)
				{
					if (Regex.IsMatch(txt, "^#"))
					{
						txt = $"{txt}## {((TitleItem)frame).Title}{Environment.NewLine}"; // insert title after a chapter title
					}
					// insert title after a verse
					else txt = $"{txt}{Environment.NewLine}{Environment.NewLine}## {((TitleItem)frame).Title}{Environment.NewLine}";
				}
				else if (frame is FootnoteItem)
				{
					txt = $"{txt} {((FootnoteItem)frame).Footnote}";
				}
				else if (frame is ParagraphItem)
				{
					txt = $"{txt}{Environment.NewLine}{Environment.NewLine}";
				}

				frame = frames.MoveNext() ? frames.Current : null;
			}

			return txt;
		}, RegexOptions.Singleline);

		// remove whitespace before a verse on a new line
		src = Regex.Replace(src, @"(?<=(^|\n))[ \t]+(\^[0-9]+\^|@[0-9]+)", "$2", RegexOptions.Singleline);

		// hack because of bad output
		// remove empty line after ## title
		src = Regex.Replace(src, @"(?<=(^|\n)##[^\r\n]*?\r?\n)([ \t]*\r?\n)+", "", RegexOptions.Singleline);
		// remove multiple emtpy lines
		src = Regex.Replace(src, @"(?<=(^|\n))([ \t]*\r?\n)+[ \t]*(?=\r?\n)", "", RegexOptions.Singleline);

		// remove bibmark footnotes
		/* replaced = true;
        while (replaced)
        {
            replaced = false;
            src = Regex.Replace(src, @"\^(?<mark>[a-zA-Z]+)\^(?!\[)(?<text>.*?)(?:\^\k<mark>(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]))[ \t]*\r?\n?", m =>
            {
                replaced = true;
                return $"{m.Groups["footnote"].Value}{m.Groups["text"].Value}";
            }, RegexOptions.Singleline);
        } */

		// apply bibmark footnotes
		replaced = true;
		int markno = 1;
		while (replaced)
		{
			replaced = false;
			src = Regex.Replace(src, @"(?<!\^[a-zA-Z]+)\^\[(?<footnote>(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!)))\](?<text>.*?)(?=\r?\n[ \t]*\r?\n)", m =>
			{
				replaced = true;
				string space;
				if (markno == 1) space = "\r\n"; else space = " ";
				return $"^{Marker(markno)}^{m.Groups["text"].Value}{space}^{Marker(markno)}^[{m.Groups["footnote"].Value}]";
			}, RegexOptions.Singleline);
			markno++;
		}

		return src;
	}

	static void ImportOutline(string path)
	{

		if (FromSource || Imported)
		{
			var mdfiles = Directory.EnumerateFiles(path, "*.md")
				.Where(file => Regex.IsMatch(Path.GetFileName(file), @"^([0-9][0-9])(?!.*?\.outline\.md)"));

			var mdtimes = mdfiles.Select(file => File.GetLastWriteTimeUtc(file));

			Books.Load(mdfiles);

			Outline items = ReadOutlines(Path.Combine(path, "src"));

			if (items.Count > 0)
			{
				Log("Importing Outline...");

				//Parallel.ForEach(mdfiles, srcfile =>
				foreach (var srcfile in mdfiles)
				{

					File.SetLastWriteTimeUtc(srcfile, DateTime.Now);
					var src = File.ReadAllText(srcfile);

					var modsrc = ApplyOutline(src, srcfile, items);

					if (src != modsrc)
					{
						File.WriteAllText(srcfile, modsrc);
						LogFile(srcfile);
					}
					//});
				}
			}
		}
	}

	public static void ImportJavascripture(string mdpath, string srcpath)
	{
		var json = System.IO.Directory.EnumerateFiles(srcpath, "*.json")
			.Select(file => File.ReadAllText(file))
			.Where(src => src.Contains("\"version\":") && src.Contains("\"versionName\":") && src.Contains("\"books\":"))
			.Select(src => JObject.Parse(src))
			.FirstOrDefault();
		if (json == null) return;
		var books = json["books"] as JObject;
		int bookno = 1;
		foreach (var book in books.Properties())
		{
			var name = book.Name;
			var bookInfo = Books[Language]?.FirstOrDefault(b => b.Key == name).Value;

			var sb = new StringBuilder();
            var phrase = new StringBuilder();
            int chapterno = 1;
			int verseno = 1;
			foreach (var chapter in book.Value as JArray)
			{
				sb.AppendLine($"# {chapterno++}");
				foreach (var versetoken in chapter as JArray) {
					string verse;
					if (versetoken is JArray versearr)
					{
						var words = versearr.Select(x => (string)(((JArray)x)[0]));
						phrase.Clear();
						foreach (var word in words)
						{
							if (phrase.Length > 0 && word != "." && word != ":"&& word != "," && word != ";" && word != "?" && word != "!" && word != "¿" && word != "¡")
							{
								phrase.Append(" ");
							}
							phrase.Append(word);
						}
						verse = phrase.ToString();
					}
					else
					{
						verse = (string)versetoken;
					}
					verse = Regex.Replace(verse, "<[0-9A-Z]+>", "");
					if (EachVerseOnNewLine) sb.AppendLine($"@{verseno++} {verse}");
					else
					{
						if (verseno > 1) sb.Append(" ");
						sb.Append($"@{verseno++} {verse}");
					}
				}
				sb.AppendLine();
			}

			string mdfile;
			if (bookInfo != null) mdfile = Path.Combine(mdpath, $"{bookInfo.Number}-{bookInfo.Name}.md");
			else mdfile = $"{bookno++}-{name}.md";
			File.WriteAllText(mdfile, sb.ToString());
			LogFile(mdfile);
		}

	}
}
