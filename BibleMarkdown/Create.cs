﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Data;

namespace BibleMarkdown;

partial class Program
{

	static void CreatePandoc(string file, string panfile)
	{
		if (IsNewer(panfile, file)) return;

		var text = File.ReadAllText(file);

		text = ApplyOutline(text, file, OutlineForCreate);

		string? bookname = Books.Name(file);
		int bookno = Books.Number(file);

		if (Replace != null && Replace.Length > 1)
		{
			var tokens = Replace.Split(Replace[0]);
			for (int i = 1; i < tokens.Length - 1; i += 2)
			{
				text = Regex.Replace(text, tokens[i], tokens[i + 1], RegexOptions.Singleline);
			}
		}

        text = Preprocess(text);

        var replmatch = Regex.Match(text, @"(/\*|//)!replace\s+(?<replace>.*?)(\*/|$)", RegexOptions.Multiline);
		if (replmatch.Success)
		{
			var s = replmatch.Groups["replace"].Value;
			if (s.Length > 4)
			{
				var tokens = s.Split(s[0]);
				for (int i = 1; i < tokens.Length - 1; i += 2)
				{
					text = Regex.Replace(text, tokens[i], tokens[i + 1]);
				}
			}
		}

		bool replaced;
		do
		{
			replaced = false;
			text = Regex.Replace(text, @"\^(?<mark>[a-zA-Z]+)\^(?!\[)(?<text>.*?)(?:\^\k<mark>(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]))[ \t]*\r?\n?", m =>
			{
				replaced = true;
				return $"{m.Groups["footnote"].Value}{m.Groups["text"].Value}";
			}, RegexOptions.Singleline);// ^^ footnotes
		} while (replaced);

		if (Regex.IsMatch(text, @"(//|/\*)!verse-paragraphs(\s|\r?\n|\*/)", RegexOptions.Singleline)) // each verse in a separate paragraph. For use in Psalms & Proverbs
		{
			text = Regex.Replace(text, @"(\^[0-9]+\^[^#]*?)(\s*?)(?=\^[0-9]+\^)", "$1\\\n", RegexOptions.Singleline);
			text = Regex.Replace(text, @"(@[0-9]+[^#]*?)(\s*?)(?=@[0-9]+)", "$1\\\n", RegexOptions.Singleline);
		}

		// text = Regex.Replace(text, @"\^([0-9]+)\^", @"\bibleverse{$1}"); // verses
		text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline); // comments
		text = Regex.Replace(text, @"//.*?\r?\n", "", RegexOptions.Multiline); // single line comments

		// text = Regex.Replace(text, @"^(# .*?)$\n^(## .*?)$", "$2\n$1", RegexOptions.Multiline); // titles
		text = Regex.Replace(text, @"\^\^", "^"); // alternative for superscript
		text = Regex.Replace(text, @"(?<!<[^\n<>]*?)""(.*?)""(?![^\n<>]>)", $"“$1”"); // replace quotation mark with nicer letters
		//text = Regex.Replace(text, @"\^([0-9]+)\^", "[$1]{.bibleverse}"); // replace bibleverses with bibleverse span.
		text = Regex.Replace(text, @"([\u0590-\u05fe]+)", "[$1]{.hebrew}");
		text = Regex.Replace(text, @"([\u0370-\u03ff\u1f00-\u1fff]+)", "[$1]{.greek}");
		text = Regex.Replace(text, @"\^([0-9]+)\^", "[$1]{.bibleverse}");
		text = Regex.Replace(text, @"@([0-9]+)", "[$1]{.bibleverse}");

		/*
		text = Regex.Replace(text, @" ^# (.*?)$", @"\chapter{$1}", RegexOptions.Multiline);
		text = Regex.Replace(text, @"^## (.*?)$", @"\section{$1}", RegexOptions.Multiline);
		text = Regex.Replace(text, @"^### (.*?)$", @"\subsection{$1}", RegexOptions.Multiline);
		text = Regex.Replace(text, @"^#### (.*)$", @"\subsubsection{$1}", RegexOptions.Multiline);
		text = Regex.Replace(text, @"\*\*(.*?)(?=\*\*)", @"\bfseries{$1}");
		text = Regex.Replace(text, @"\*([^*]*)\*", @"\emph{$1}", RegexOptions.Singleline); 
		text = Regex.Replace(text, @"\^\[([^\]]*)\]", @"\footnote{$1}", RegexOptions.Singleline);
		*/

		File.WriteAllText(panfile, text);
		LogFile(panfile);
	}

	static async Task CreateTeXAsync(string mdfile, string texfile)
	{
		if (IsNewer(texfile, mdfile)) return;

		var mdtexfile = Path.Combine(Path.GetDirectoryName(mdfile), "tex", Path.GetFileName(mdfile));
		var book = Regex.Match(mdfile, "[0-9.]+(?=-.*\\.md$)").Value.Replace('.', '-');
		var src = File.ReadAllText(mdfile);
        src = Regex.Replace(src, @"\[([0-9]+)\]\{\.bibleverse\}", @"\bibleverse{$1}");
		src = Regex.Replace(src, @"\[([\u0590-\u05fe]+)\]\{\.hebrew\}", @"\hebrew{$1}");
		src = Regex.Replace(src, @"\[([\u0370-\u03ff\u1f00-\u1fff]+)\]\{\.greek\}", @"\greek{$1}");
		src = Regex.Replace(src, @"\[(.+?)\]\{.wj\}", @"\wordsOfJesus{$1}"); // words of Jesus
		src = Regex.Replace(src, @"^(# .*?)$\n^(## .*?)$", "$2\n$1", RegexOptions.Multiline); // titles
		src = Regex.Replace(src, @"^# ([0-9]+)\s*$", $"\\hypertarget{{section-{book}-$1}}{{%\n\\section{{$1}}\\label{{section-{book}-$1}}}}",
			RegexOptions.Multiline); // section hyperlinks
		File.WriteAllText(mdtexfile, src);
		LogFile(mdtexfile);

		if (!string.IsNullOrWhiteSpace(src)) {
			await Pandoc.RunAsync(mdtexfile, texfile, "markdown-smart", "latex");
			src = File.ReadAllText(texfile);
			src = Regex.Replace(src, @"\\nopandoc\{((?>\{(?<c>)|[^\{\}]+|\}(?<-c>))*(?(c)(?!)))\}", "$1");
			File.WriteAllText(texfile, src);
			LogFile(texfile);
		}
	}

	static async Task CreateHtmlAsync(string mdfile, string htmlfile)
	{
		if (IsNewer(htmlfile, mdfile) || TwoLanguage) return;

		//var mdhtmlfile = Path.ChangeExtension(mdfile, ".html.md");

		//File.Copy(mdfile, mdhtmlfile);
		var src = File.ReadAllText(mdfile);
        //src = Regex.Replace(src, @"\^([0-9]+)\^", "<sup class='bibleverse'>$1</sup>", RegexOptions.Singleline);
        //File.WriteAllText(mdhtmlfile, src);

        if (!string.IsNullOrWhiteSpace(src))
		{
			await Pandoc.RunAsync(mdfile, htmlfile, "markdown-smart", "html");
			LogFile(htmlfile);
        }
	}


	static string Id(string name)
	{
		return name.Replace(' ', '-').Replace('.', '-');
	}

	static void CreateTwoLanguage(string path, string path1, string path2)
	{

		Log("Create Two Langauge...");
		var leftfiles = Directory.EnumerateFiles(path1, "*.md")
			.Select(file => new
			{
				Name = Books.Name(file),
				Number = Books.Number(file),
				File = file,
				Book = Books[LeftLanguage].ContainsKey(Books.Name(file)) ? Books[LeftLanguage][Books.Name(file)] : null,
				Text = File.ReadAllText(file),
			})
			.Where(book => book.Book != null)
			.OrderBy(book => book.Number)
			.ToArray();

		var rightfiles = Directory.EnumerateFiles(path2, "*.md")
			.Select(file => new
			{
				Name = Books.Name(file),
				Number = Books.Number(file),
				File = file,
				Book = Books[RightLanguage].ContainsKey(Books.Name(file)) ? Books[RightLanguage][Books.Name(file)] : null,
				Text = File.ReadAllText(file),
			})
			.Where(book => book.Book != null)
			.ToDictionary(book => book.Number);

		var books = leftfiles
			.Select(file => new
			{
				Left = file,
				Right = rightfiles.ContainsKey(file.Number) ? rightfiles[file.Number] : null,
				New = Path.Combine(path, $"{file.Number:d2}-{file.Name}.md")
			})
			.Where(book => book.Right != null && !IsNewer(book.New, book.Left.File) && !IsNewer(book.New, book.Right.File));

		foreach (var book in books)
		{

			var leftchapters = Regex.Matches(book.Left.Text, @"(?<=(^|\n))#[ \t]+(?<chapter>[0-9]+)[ \t]*\r?\n(?<text>.*?)(?=\r?\n#[ \t]+[0-9]+|\s*$)", RegexOptions.Singleline)
				.Select(match => new
				{
					Chapter = int.Parse(match.Groups["chapter"].Value),
					Text = match.Groups["text"].Value
				});
			var rightchapters = Regex.Matches(book.Right.Text, @"(?<=(^|\n))#[ \t]+(?<chapter>[0-9]+)[ \t]*\r?\n(?<text>.*?)(?=\r?\n#[ \t]+[0-9]+|\s*$)", RegexOptions.Singleline)
				.Select(match => new
				{
					Chapter = int.Parse(match.Groups["chapter"].Value),
					Text = match.Groups["text"].Value
				})
				.ToDictionary(chapter => chapter.Chapter);
			var text = new StringBuilder();
			text.AppendLine(@" \nopandoc{\begin{paracol}{2}}");
			foreach (var chapter in leftchapters)
			{
				text.AppendLine($@"\switchcolumn[0]*{Environment.NewLine}{Environment.NewLine}# {chapter.Chapter}");
				text.AppendLine(chapter.Text);
				text.AppendLine($@"\switchcolumn");
				text.AppendLine($@"\nopandoc{{\begin{{otherlanguage}}{{{RightLanguage.ToLower()}}}}}");
				text.AppendLine($@"{Environment.NewLine}# {chapter.Chapter}");
				text.AppendLine(rightchapters[chapter.Chapter].Text);
				text.AppendLine(@"\nopandoc{\end{otherlanguage}}");
			}
			text.AppendLine(@"\nopandoc{\end{paracol}}");

			var newfile = Path.Combine(path, $"{book.Left.Number:d2}-{book.Left.Name}.md");
			File.WriteAllText(newfile, text.ToString());
			LogFile(newfile);
		}
	}
	static void CreateVerseStats(string path)
	{
		var sources = Directory.EnumerateFiles(path, "*.md")
			.Where(file => Regex.IsMatch(Path.GetFileName(file), "^([0-9][0-9])"));
		var verses = new StringBuilder();

		var frames = Path.Combine(path, @"out", "verseinfo.md");
		var frametime = DateTime.MinValue;
		if (File.Exists(frames)) frametime = File.GetLastWriteTimeUtc(frames);

		if (sources.All(src => File.GetLastWriteTimeUtc(src) < frametime) && frametime > bibmarktime) return;

		bool firstsrc = true;
		int btotal = 0;
		foreach (var source in sources)
		{

			if (!firstsrc) verses.AppendLine();
			firstsrc = false;
			verses.AppendLine($"# {Path.GetFileName(source)}");

			var txt = File.ReadAllText(source);

			int chapter = 0;
			int verse = 0;
			int nverses = 0;
			int totalverses = 0;
			var matches = Regex.Matches(txt, @"((^|\n)#\s+(?<chapter>[0-9]+))|(\^(?<verse>[0-9]+)\^(?!\s*[#\^@$]))|(@(?<verse2>[0-9]+)(?!\s*[#\^@$]))", RegexOptions.Singleline);
			foreach (Match m in matches)
			{
				if (m.Groups[1].Success)
				{
					int.TryParse(m.Groups["chapter"].Value, out chapter);
					if (verse != 0)
					{
						verses.Append(verse);
						verses.Append(' ');
					}
					verses.Append(chapter); verses.Append(':');
					totalverses += nverses;
					nverses = 0;
				}
				else if (m.Groups["verse"].Success)
				{
					int.TryParse(m.Groups["verse"].Value, out verse);
					nverses = Math.Max(nverses, verse);

				}
				else if (m.Groups["verse2"].Success)
				{
					int.TryParse(m.Groups["verse2"].Value, out verse);
					nverses = Math.Max(nverses, verse);

				}
			}
			if (verse != 0) verses.Append(verse);
			totalverses += nverses;
			nverses = 0;
			verses.Append("; Total verses:"); verses.Append(totalverses);
			btotal += totalverses;
			totalverses = 0;
			nverses = 0;
			verse = 0;
			chapter = 0;
		}

		verses.AppendLine(); verses.AppendLine(); verses.AppendLine(btotal.ToString());

		File.WriteAllText(frames, verses.ToString());
		LogFile(frames);
	}

	static void CreateOutline(string path)
	{
		var sources = Directory.EnumerateFiles(path, "*.md")
			.Where(file => Regex.IsMatch(Path.GetFileName(file), @"^([0-9][0-9])(?!.*?\.outline\.md)"));
		var verses = new StringBuilder();

		var framesfile = Path.Combine(path, "out", "outline.md");
		var frametime = DateTime.MinValue;
		if (File.Exists(framesfile)) frametime = File.GetLastWriteTimeUtc(framesfile);

		if (!sources.Any() || sources.All(src => File.GetLastWriteTimeUtc(src) < frametime) && frametime > bibmarktime) return;

		var items = new Outline();

        Books.Load(sources);

		foreach (var source in sources)
		{
			int bookno = Books.Number(source);
			string bookname = Books.Name(source);

			var book = Books[Language, bookname];

			var bookItem = new BookItem(book, Path.GetFileName(source));

            items.Add(bookItem);

			var txt = File.ReadAllText(source);

			// remove bibmark footnotes
			bool replaced;
			do
			{
				replaced = false;
				txt = Regex.Replace(txt, @"\^(?<mark>[a-zA-Z]+)\^(?!\[)(?<text>.*?)(?:\^\k<mark>(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]))[ \t]*\r?\n?", m =>
				{
					replaced = true;
					return $"{m.Groups["footnote"].Value}{m.Groups["text"].Value}";
				}, RegexOptions.Singleline);
			} while (replaced);

			bookItem.VerseParagraphs = Regex.IsMatch(txt, "(//|/\\*)!verse-paragraphs.*?($|\\*/|\\r?\\n)", RegexOptions.Singleline);

			int chapterno = 0;
			var chapters = Regex.Matches(txt, @"(?<!#)#(?!#)(\s*(?<chapter>[0-9]+).*?)\r?\n(?<text>.*?)(?=(?<!#)#(?!#)|$)", RegexOptions.Singleline);
			foreach (Match chapter in chapters)
			{
				chapterno++;
				int.TryParse(chapter.Groups["chapter"].Value, out chapterno);

				var chapterItem = new ChapterItem(book, chapterno);
				items.Add(chapterItem);
				bookItem.Items.Add(chapterItem);

				var chaptertext = chapter.Groups["text"].Value;

				var tokens = Regex.Matches(chaptertext,
					@"\^(?<verse>[0-9]+)\^|@(?<verse2>[0-9]+)|(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\])(?=(?<endofverse>\s*?((\^[0-9]+\^|@[0-9]+)|\n#|$)))|(?<=\r?\n)(?<blank>\r?\n)(?!\s*?(?:\^[a-zA-Z]+\^\[|#|$))(?=\s*\^[0-9]+\^|\s*@[0-9]+)|(?<=\r?\n|^)##(?<title>.*?)(?=\r?\n|$)",
					RegexOptions.Singleline);
				int verse = -1;

				foreach (Match token in tokens)
				{
					if (token.Groups["verse"].Success) verse = int.Parse(token.Groups["verse"].Value);
					else if (token.Groups["verse2"].Success) verse = int.Parse(token.Groups["verse2"].Value);
					else if (token.Groups["blank"].Success)
					{
						var item = new ParagraphItem(book, chapterItem.Chapter, verse);
						items.Add(item);
						bookItem.Items.Add(item);
					}
					else if (token.Groups["title"].Success)
					{
						var item = new TitleItem(book, token.Groups["title"].Value, chapterItem.Chapter, verse);
						items.Add(item);
						bookItem.Items.Add(item);
					}
					if (verse == -1) verse = 0;
				}
			}
		}

		var append = ReadOutlines(path);
		if (!append.Append && append.Count > 0)
		{
			items = append;
		}
		else if (append.Append && append.Count > 0)
		{
			items = new Outline(items.Concat(append));
			items.Sort();
		}
        items.Save(framesfile);
    }

    static void CreateUSFM(string mdfile, string usfmfile)
	{
		if (IsNewer(usfmfile, mdfile) || TwoLanguage) return;

		string usfm = "";
		if (File.Exists(usfmfile)) usfm = File.ReadAllText(usfmfile);

		var name = Books.Name(mdfile);
		var txt = File.ReadAllText(mdfile);

		if (string.IsNullOrEmpty(usfm)) txt = @$"\h {name}{Environment.NewLine}\toc1 {name}{Environment.NewLine}{Environment.NewLine}\rem From here on, this file is autogenerated by bibmark. You may edit the header section, as it will not be changed by bibmark.{Environment.NewLine}{Environment.NewLine}{txt}";
		txt = Regex.Replace(txt, @"(^|\n)#[ \t]+([0-9]+)", @$"\c $2{Environment.NewLine}\p", RegexOptions.Singleline);
		txt = Regex.Replace(txt, @"(^|\n)##[ \t]+([^\r\n]*?)\r?\n", @$"\s1 $2{Environment.NewLine}\p", RegexOptions.Singleline);
		txt = Regex.Replace(txt, @"(?<!^|\n)\[([0-9]+)\]\{\.bibleverse\}", $@"{Environment.NewLine}\v $1", RegexOptions.Singleline);
		txt = Regex.Replace(txt, @"\[([0-9]+)\]\{\.bibleverse\}", @"\v $1", RegexOptions.Singleline);
		txt = Regex.Replace(txt, @"\[([\u0590-\u05fe]+)\]\{\.hebrew\}", @"$1");
		txt = Regex.Replace(txt, @"\[([\u0370-\u03ff\u1f00-\u1fff]+)\]\{\.greek\}", @"$1");
		txt = Regex.Replace(txt, @"\*", "", RegexOptions.Singleline); // remove italics
		txt = Regex.Replace(txt, @"\[([^]]*)\]\{\.smallcaps\}", @"\sc $1\sc*", RegexOptions.Singleline); // smallcaps
		txt = Regex.Replace(txt, @"\[([^]]*)\]\{\.wj\}", @"\wj $1\wj*", RegexOptions.Singleline); // words of Jesus
		txt = Regex.Replace(txt, @"\\\*(.*?)\*\\", m =>
		{
			var lines = m.Groups[1].Value.Split('\n')
				.Select(line => $"\\rem {line.Trim()}");
			return $"{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
		}, RegexOptions.Singleline); // comments
		txt = Regex.Replace(txt, @"//(.*?\r?\n)", "\\rem $1", RegexOptions.Singleline); // single line comments
                                                                                
		// remove bibmark footnotes.
            bool replaced = true;
		while (replaced)
		{
			replaced = false;
			txt = Regex.Replace(txt, @"\^(?<mark>[a-zA-Z]+)\^(?!\[)(?<text>.*?)(?:\^\k<mark>(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]))[ \t]*\r?\n?", m =>
			{
				replaced = true;
				return $"{m.Groups["footnote"].Value}{m.Groups["text"].Value}";
			}, RegexOptions.Singleline);
		}
		txt = Regex.Replace(txt, @"\^\[\s*(?<footpos>[0-9]+[:,][0-9]+)\s*(?<foottext>.*?)\s*\]", @"\f + \fr ${footpos} \ft ${foottext} \f*", RegexOptions.Singleline);
		txt = Regex.Replace(txt, @"(\r?\n)([ \t]*)(\r?\n)", @"$1\p$3", RegexOptions.Singleline); // paragraphs
		var header = Regex.Match(usfm, @"^.*?(?=\s*\\c)", RegexOptions.Singleline).Value.Trim();
		txt = header + Environment.NewLine + Environment.NewLine + txt;

		File.WriteAllText(usfmfile, txt);
		LogFile(usfmfile);
	}
	static string Marker(int n)
	{
		StringBuilder s = new StringBuilder();
		while (n > 0)
		{
			s.Append((char)((int)'a' + n % 26 - 1));
			n = n / 26;
		}
		return s.ToString();
	}


}
