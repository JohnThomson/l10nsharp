﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using L10NSharp.XLiffUtils;
using L10NSharp.UI;
using NUnit.Framework;

namespace L10NSharp.Tests
{
	[TestFixture]
	public class LocalizationManagerTests
	{
		private const string AppId = "test";
		private const string AppName = "unit test";
		private const string AppVersion = "1.0.0";
		private const string HigherVersion = "2.0.0";
		private const string LowerVersion = "0.0.1";
		private const string LiteralNewline = "\\n";

		[TearDown]
		public void TearDownLocalizationManagers()
		{
			LocalizationManager.UseLanguageCodeFolders = false;
			LocalizationManager.LoadedManagers.Clear();
			LocalizationManager.SetUILanguage(LocalizationManager.kDefaultLang, false);
		}

		/// <summary>
		/// If there is no GeneratedDefault Xliff file, but the file we need has been installed, copy the installed version to circumvent
		/// a crash trying to generate this Xliff file on Linux.
		/// </summary>
		[Test]
		public void CreateOrUpdateDefaultXliffFileIfNecessary_CopiesInstalledIfAvailable()
		{
			using(var folder = new TempFolder("CreateOrUpdate_CopiesInstalledIfAvailable"))
			{
				// SUT (buried down in there somewhere)
				SetupManager(folder);

				// verify presence and identicality of the generated file to the installed file
				var filename = LocalizationManager.GetXliffFileNameForLanguage(AppId, LocalizationManager.kDefaultLang);
				var installedFilePath = Path.Combine(GetInstalledDirectory(folder), filename);
				var generatedFilePath = Path.Combine(GetGeneratedDirectory(folder), filename);
				Assert.That(File.Exists(generatedFilePath), "Generated file {0} should exist", generatedFilePath);
				FileAssert.AreEqual(installedFilePath, generatedFilePath, "Generated file should be copied from and identical to Installed file");
			}
		}

		/// <summary>
		/// On Linux, we crash trying to generate XLIFF files, leaving an empty file in the Generated folder.
		/// Copy the installed file over the empty generated file in this case.
		/// </summary>
		[Test]
		public void CreateOrUpdateDefaultXliffFileIfNecessary_CopiesOverEmptyGeneratedFile()
		{
			using(var folder = new TempFolder("CreateOrUpdate_CopiesOverEmptyGeneratedFile"))
			{
				var filename = LocalizationManager.GetXliffFileNameForLanguage(AppId, LocalizationManager.kDefaultLang);
				var installedFilePath = Path.Combine(GetInstalledDirectory(folder), filename);
				var generatedFilePath = Path.Combine(GetGeneratedDirectory(folder), filename);

				// generate an empty English Xliff file
				Directory.CreateDirectory(GetGeneratedDirectory(folder));
				var fileStream = File.Open(generatedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
				fileStream.Close();

				// SUT (buried down in there somewhere)
				SetupManager(folder);

				// verify identicality of the generated file to the installed file
				FileAssert.AreEqual(installedFilePath, generatedFilePath, "Generated file should be copied from and identical to Installed file");
			}
		}

		/// <summary>
		/// If there is an existing Xliff file of the same (or higher) version, it should not be overwritten when initializing the localizer.
		/// </summary>
		[Test]
		public void CreateOrUpdateDefaultXliffFileIfNecessary_DoesNotOverwriteUpToDateGeneratedFile()
		{
			using(var folder = new TempFolder("CreateOrUpdate_DoesNotOverwriteUpToDateGeneratedFile"))
			{
				var filename = LocalizationManager.GetXliffFileNameForLanguage(AppId, LocalizationManager.kDefaultLang);
				var generatedFilePath = Path.Combine(GetGeneratedDirectory(folder), filename);

				// "generate" an English Xliff for a higher version
				Directory.CreateDirectory(GetGeneratedDirectory(folder));
				AddEnglishXliff(GetGeneratedDirectory(folder), HigherVersion);
				var generatedXliffContents = File.ReadAllText(generatedFilePath);

				// SUT (buried down in there somewhere)
				SetupManager(folder);

				// verify identicality of the generated file to its previous state
				Assert.AreEqual(generatedXliffContents, File.ReadAllText(generatedFilePath), "Generated file should not have been overwritten");
			}
		}

		/// <summary>
		/// If the GeneratedDefault Xliff file is out of date, it should be brought up to the current version
		/// (sorry, this doesn't test the contents, just the version number).
		/// </summary>
		[Test]
		public void CreateOrUpdateDefaultXliffFileIfNecessary_OverwritesOutdatedGeneratedFile()
		{
			using(var folder = new TempFolder("CreateOrUpdate_OverwritesOutdatedGeneratedFile"))
			{
				var filename = LocalizationManager.GetXliffFileNameForLanguage(AppId, LocalizationManager.kDefaultLang);
				var generatedFilePath = Path.Combine(GetGeneratedDirectory(folder), filename);

				// "generate" an English Xliff for a lower version
				Directory.CreateDirectory(GetGeneratedDirectory(folder));
				AddEnglishXliff(GetGeneratedDirectory(folder), LowerVersion);

				// SUT (buried down in there somewhere)
				SetupManager(folder);

				// verify that the generated file has been updated to the current version
				var xmlDoc = XElement.Load(generatedFilePath);
				var docNamespace = xmlDoc.GetDefaultNamespace();
				var fileElt = xmlDoc.Element(docNamespace + "file");
				Assert.NotNull(fileElt);
				var generatedVersion = fileElt.Attribute("product-version").Value;
				Assert.AreEqual(new Version(AppVersion).ToString(), generatedVersion, "Generated file should have been updated to the current version");
			}
		}

		/// <summary>
		/// This is a regression test. As of Nov 2014, if we updated an English string, it would
		/// get overwritten by the old version, found in another language's Xliff.
		/// </summary>
		[Test]
		public void GetDynamicString_EnglishAndArabicHaveDifferentValuesOfEnglishString_EnglishXliffWins()
		{
			using(var folder = new TempFolder("GetString_EnglishAndArabicHaveDifferentValuesOfEnglishString_EnglishXliffWins"))
			{
				SetupManager(folder, "en");
				//This was the original assertion, and it worked:
				//   Assert.AreEqual("from English Xliff", LocalizationManager.GetDynamicString(AppId, "theId", "some default"));
				//However, later I decided, I don't care what is in the English Xliff, either. If the c# code just gave a new
				// value for this, what the c# code said should win. Who cares what's in the English Xliff. It's only real
				// purpose in life is to provide a list of strings that have been disovered dynamically when the translator
				// needs a list of strings to translate (without it, the translator would have to cause the program to visit
				// each part of the UI so as to trip over all the GetDynamicString() calls.

				//So now, this is correct:

				Assert.AreEqual("from the c# code", LocalizationManager.GetDynamicString(AppId, "theId", "from the c# code"));
			}
		}
		/// <summary>
		/// This is a regression test. If the English Xliff is out of date, too bad. The c# code always wins.
		/// </summary>
		[Test]
		public void GetDynamicString_EnglishXliffHasDIfferentStringThanParamater_ParameterWins()
		{
			using(var folder = new TempFolder("GetDynamicString_EnglishXliffHasDIfferentStringThanParameter_ParameterWins"))
			{
				SetupManager(folder, "en");
				Assert.AreEqual("from c# code", LocalizationManager.GetDynamicString(AppId, "theId", "from c# code"));
			}
		}

		[Test]
		public void GetDynamicString_ArabicXliffHasArabicValue_ArabicXliffWins()
		{
			using(var folder = new TempFolder("GetDynamicString_EnglishXliffHasDIfferentStringThanParameter_ParameterWins"))
			{
				SetupManager(folder, "ar");
				Assert.AreEqual("inArabic", LocalizationManager.GetDynamicString(AppId, "theId", "from c# code"));
			}
		}
		[Test]
		public void GetDynamicStringOrEnglish()
		{
			using(var folder = new TempFolder("GetDynamicString_EnglishXliffHasDIfferentStringThanParameter_ParameterWins"))
			{
				SetupManager(folder, "ar");
				Assert.AreEqual("blahInEnglishInCode", GetBlah("en"), "If asked for English, should give whatever is in the code.");
				Assert.AreEqual("blahInEnglishInCode", GetBlah("ar"), "We don't have Arabic so should get the English from code.");
				LocalizationManager.SetUILanguage("fr", true);
				Assert.AreEqual("blahInEnglishInCode", GetBlah("en"), "If asked for English, should give whatever is in the code.");
				Assert.AreEqual("blahInFrench", GetBlah("fr"), "We do have French for this, should have found it.");
			}
		}

		private string GetBlah(string langId)
		{
			return LocalizationManager.GetDynamicStringOrEnglish(AppId, "blahId", "blahInEnglishInCode", "comment", langId);
		}

		[Test]
		public void GetDynamicStringInEnglish_NoDefault_FindsEnglish()
		{
			using (var folder = new TempFolder("GetDynamicStringInEnglish_NoDefault_FindsEnglish"))
			{
				SetupManager(folder, "en");
				Assert.That(LocalizationManager.GetDynamicString(AppId, "blahId", null), Is.EqualTo("blah"), "With no default supplied, should find saved English");
			}
		}

		[Test]
		public void GetDynamicStringOrEnglish_LmDisposed_GivesUsefulException()
		{
			using (var folder = new TempFolder("GetDynamicStringOrEnglish_LmDisposed_GivesUsefulException"))
			{
				SetupManager(folder, "en");
				Assert.That(LocalizationManager.GetDynamicString(AppId, "blahId", null), Is.EqualTo("blah"), "With no default supplied, should find saved English");
				var extra = new LocalizationManager("nonsense", "more nonsense", "1.0");
				LocalizationManager.LoadedManagers.Add(extra.Id, extra);
				LocalizationManager.LoadedManagers[AppId].Dispose();
				Assert.Throws<ObjectDisposedException>(() => LocalizationManager.GetDynamicString(AppId, "blahId", null));
				extra.Dispose();
				// A different path when there are none left
				Assert.Throws<ObjectDisposedException>(() => LocalizationManager.GetDynamicString(AppId, "blahId", null));
			}
		}

		//NOTE: the TestName parameter is only here to work around an NUnit bug in which
		//NUnit doesn't run alll the test cases when some differ only by the values in an array parameter
		//cases where we expect to get back the english in the code
		[TestCase(new[] { "en" }, "blahInEnglishCode", "en", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_1")]
		[TestCase(new[] { "en", "fr" }, "blahInEnglishCode", "en", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_2")]
		[TestCase(new[] { "ar", "en" }, "blahInEnglishCode", "en", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_3")] // our arabic doesn't have a translation of 'blah', so fall to the code's English
		[TestCase(new[] { "zz", "en", "fr" }, "blahInEnglishCode", "en", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_4")]
		//cases where we expect to get back the French
		[TestCase(new[] { "fr" }, "blahInFrench", "fr", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_5")]
		[TestCase(new[] { "fr", "en" }, "blahInFrench", "fr", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_6")]
		[TestCase(new[] { "ar", "fr", "en" }, "blahInFrench", "fr", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_7")] // our arabic doesn't have a translation of 'blah', so fall to French
		public void GetString_OverloadThatTakesListOfLanguages_Works(IEnumerable<string> preferredLangIds,  string expectedResult, string expectedLanguage)
		{
			using(var folder = new TempFolder("GetString"))
			{
				AddRandomXliff("ii", GetInstalledDirectory(folder));
				SetupManager(folder, "ii" /* UI language not important */);
				string languageFound;
				var result = LocalizationManager.GetString("blahId", "blahInEnglishCode", "comment", preferredLangIds, out languageFound);
				Assert.AreEqual(expectedResult, result);
				Assert.AreEqual(expectedLanguage, languageFound);
			}
		}

		[Test]
		public void GetUiLanguages_EnglishIsThere()
		{
			var cultures = LocalizationManager.GetUILanguages(false);
			Assert.AreEqual("English", cultures.Where(c => c.Name == "en").Select(c => c.NativeName).FirstOrDefault());
		}

		[Test]
		public void GetUiLanguages_FindsAll()
		{
			using (var folder = new TempFolder("AllLanguages"))
			{
				SetupManager(folder);
				var cultures = new List<L10NCultureInfo>(LocalizationManager.GetUILanguages(true));
				Assert.AreEqual(4, cultures.Count);
				Assert.AreEqual("ar", cultures[0].IetfLanguageTag);		// Arabic
				Assert.AreEqual("en", cultures[1].IetfLanguageTag);		// English
				Assert.AreEqual("fr", cultures[2].IetfLanguageTag);		// French
				Assert.AreEqual("es", cultures[3].IetfLanguageTag);		// Spanish
			}
		}

		[Test]
		public void GetUiLanguages_FindsAllWithFolders()
		{
			try
			{
				LocalizationManager.UseLanguageCodeFolders = true;
				using (var folder = new TempFolder("AllLanguages"))
				{
					SetupManager(folder);
					var cultures = new List<L10NCultureInfo>(LocalizationManager.GetUILanguages(true));
					Assert.AreEqual(4, cultures.Count);
					Assert.AreEqual("ar", cultures[0].IetfLanguageTag);		// Arabic
					Assert.AreEqual("en", cultures[1].IetfLanguageTag);		// English
					Assert.AreEqual("fr", cultures[2].IetfLanguageTag);		// French
					Assert.AreEqual("es", cultures[3].IetfLanguageTag);		// Spanish
				}
			}
			finally
			{
				LocalizationManager.UseLanguageCodeFolders = false;
			}
		}

		int compareCultureTags(CultureInfo first, CultureInfo second)
		{
			return first.IetfLanguageTag.CompareTo(second.IetfLanguageTag);
		}

		[Test]
		public void GetDynamicStringInEnglish_NoDefault_FindsEnglishWithFolders()
		{
			try
			{
				LocalizationManager.UseLanguageCodeFolders = true;
				using (var folder = new TempFolder("GetDynamicStringInEnglish_NoDefault_FindsEnglish"))
				{
					SetupManager(folder);
					Assert.That(LocalizationManager.GetDynamicString(AppId, "blahId", null), Is.EqualTo("blah"), "With no default supplied, should find saved English");
				}
			}
			finally
			{
				LocalizationManager.UseLanguageCodeFolders = false;
			}
		}

		//NOTE: the TestName parameter is only here to work around an NUnit bug in which
		//NUnit doesn't run alll the test cases when some differ only by the values in an array parameter
		//cases where we expect to get back the english in the code
		[TestCase(new[] { "en" }, "blahInEnglishCode", "en", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_1")]
		[TestCase(new[] { "en", "fr" }, "blahInEnglishCode", "en", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_2")]
		[TestCase(new[] { "ar", "en" }, "blahInEnglishCode", "en", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_3")] // our arabic doesn't have a translation of 'blah', so fall to the code's English
		[TestCase(new[] { "zz", "en", "fr" }, "blahInEnglishCode", "en", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_4")]
		//cases where we expect to get back the French
		[TestCase(new[] { "fr" }, "blahInFrench", "fr", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_5")]
		[TestCase(new[] { "fr", "en" }, "blahInFrench", "fr", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_6")]
		[TestCase(new[] { "ar", "fr", "en" }, "blahInFrench", "fr", TestName = "GetString_OverloadThatTakesListOfLanguages_Works_7")] // our arabic doesn't have a translation of 'blah', so fall to French
		public void GetString_OverloadThatTakesListOfLanguages_WorksWithFolders(IEnumerable<string> preferredLangIds,  string expectedResult, string expectedLanguage)
		{
			LocalizationManager.UseLanguageCodeFolders = true;
			using (var folder = new TempFolder("GetString"))
			{
				AddRandomXliff("ii", GetInstalledDirectory(folder));
				SetupManager(folder, "ii" /* UI language not important */);
				string languageFound;
				var result = LocalizationManager.GetString("blahId", "blahInEnglishCode", "comment", preferredLangIds, out languageFound);
				Assert.AreEqual(expectedResult, result);
				Assert.AreEqual(expectedLanguage, languageFound);
			}
		}

		[Test]
		public void GetUiLanguages_AzeriHasHackedNativeName()
		{
			// Check if the OS includes 'az' culture - Ubuntu 12.04 Precise doesn't
			if (!CultureInfo.GetCultures(CultureTypes.NeutralCultures).Any(c => c.Name == "az"))
				Assert.Ignore("Test requires the availability of the 'az' culture");

			var cultures = LocalizationManager.GetUILanguages(false);
			Assert.AreEqual("Azərbaycan dili", cultures.Where(c => c.Name == "az").Select(c => c.NativeName).FirstOrDefault());
		}

		/// <summary>
		/// - "Installs" English, Arabic, and French
		/// - Sets the UI language
		/// - Constructs a LocalizationManager and adds it to LocalizationManager.LoadedManagers[AppId]
		/// </summary>
		public static void SetupManager(TempFolder folder, string uiLanguageId = LocalizationManager.kDefaultLang)
		{
			LocalizationManager.LoadedManagers.Clear();
			AddEnglishXliff(GetInstalledDirectory(folder), AppVersion);
			AddArabicXliff(GetInstalledDirectory(folder));
			AddFrenchXliff(GetInstalledDirectory(folder));
			AddSpanishXliff(GetInstalledDirectory(folder));

			LocalizationManager.SetUILanguage(uiLanguageId, true);
			var manager = new LocalizationManager(AppId, AppName, AppVersion,
				GetInstalledDirectory(folder), GetGeneratedDirectory(folder), GetUserModifiedDirectory(folder));

			// REVIEW (Hasso) 2015.01: Since AppId is static, I wonder what conflicts this may cause (even though each test has its own TempFolder)
			LocalizationManager.LoadedManagers[AppId] = manager;
		}

		private static string GetInstalledDirectory(TempFolder parentDir)
		{
			return parentDir.Path; // no reason we can't use the root temp dir for the "installed" Xliff's
		}

		private static string GetGeneratedDirectory(TempFolder parentDir)
		{
			return parentDir.Combine("generated");
		}

		private static string GetUserModifiedDirectory(TempFolder parentDir)
		{
			return parentDir.Combine("userModified");
		}

		private static void AddEnglishXliff(string folderPath, string appVersion)
		{
			var englishDoc = new XLiffDocument { File = {SourceLang = "en"}};
			if (!String.IsNullOrEmpty(appVersion))
				englishDoc.File.ProductVersion = appVersion;
			englishDoc.File.HardLineBreakReplacement = LiteralNewline;
			englishDoc.File.Original = "test.dll";
			// first unit
			var tu = new TransUnit
			{
				Id = "theId",
				Source = new TransUnitVariant {Lang = "en", Value = "from English Xliff"},
				Notes = { new XLiffNote {Text = "Test"} }
			};
			englishDoc.AddTransUnit(tu);
			// second unit
			var tu2 = new TransUnit
			{
				Id = "notUsedId",
				Source = new TransUnitVariant {Lang = "en", Value = "no longer used English text"}
			};
			englishDoc.AddTransUnit(tu2);
			// third unit
			var tu3 = new TransUnit
			{
				Id = "blahId",
				Source = new TransUnitVariant {Lang = "en", Value = "blah"}
			};
			englishDoc.AddTransUnit(tu3);
			englishDoc.Save(Path.Combine(folderPath, LocalizationManager.GetXliffFileNameForLanguage(AppId, "en")));
		}

		private static void AddArabicXliff(string folderPath)
		{
			var arabicDoc = new XLiffDocument { File = {SourceLang = "en", TargetLang="ar"}};
			arabicDoc.File.Original = "test.dll";
			// first unit
			var tu = new TransUnit
			{
				Id = "theId",
				Source = new TransUnitVariant {Lang = "en", Value = "wrong"},
				Target = new TransUnitVariant {Lang = "ar", Value = "inArabic"},
				Notes = { new XLiffNote {Text = "Test"} }
			};
			arabicDoc.AddTransUnit(tu);
			// second unit
			var tu2 = new TransUnit
			{
				Id = "notUsedId",
				Source = new TransUnitVariant {Lang = "en", Value = "inEnglishpartofArabicXliff"},
				Target = new TransUnitVariant {Lang = "ar", Value = "inArabic"}
			};
			arabicDoc.AddTransUnit(tu2);
			arabicDoc.Save(Path.Combine(folderPath, LocalizationManager.GetXliffFileNameForLanguage(AppId, "ar")));
		}

		private static void AddFrenchXliff(string folderPath)
		{
			var doc = new XLiffDocument { File = {SourceLang = "en", TargetLang = "fr"}};
			doc.File.Original = "test.dll";
			// first unit
			var tu = new TransUnit
			{
				Id = "blahId",
				Source = new TransUnitVariant {Lang = "en", Value = "blah"},
				Target = new TransUnitVariant {Lang = "fr", Value = "blahInFrench"},
				Notes = { new XLiffNote {Text = "Test"} }
			};
			tu.Dynamic = true;
			doc.AddTransUnit(tu);
			doc.Save(Path.Combine(folderPath, LocalizationManager.GetXliffFileNameForLanguage(AppId, "fr")));
		}

		internal static void AddSpanishXliff(string folderPath)
		{
			var spanishDoc = new XLiffDocument { File = {SourceLang = "en", TargetLang="es"}};
			spanishDoc.File.HardLineBreakReplacement = LiteralNewline;
			spanishDoc.File.Original = "test.dll";
			// first unit
			var tu = new TransUnit
			{
				Id = "theId",
				Source = new TransUnitVariant {Lang = "en", Value = "from English Xliff"},
				Target = new TransUnitVariant {Lang = "es", Value = "from Spanish Xliff"},
				Notes = { new XLiffNote {Text = "Test"} }
			};
			spanishDoc.AddTransUnit(tu);
			// second unit
			var tu2 = new TransUnit
			{
				Id = "notUsedId",
				Source = new TransUnitVariant {Lang = "en", Value = "no longer used English text"},
				Target = new TransUnitVariant {Lang = "es", Value = "no longer used Spanish text"}
			};
			spanishDoc.AddTransUnit(tu2);
			// third unit
			var tu3 = new TransUnit
			{
				Id = "blahId",
				Source = new TransUnitVariant {Lang = "en", Value = "blah"},
				Target = new TransUnitVariant {Lang = "es", Value = "bleah"}
			};
			spanishDoc.AddTransUnit(tu3);
			spanishDoc.Save(Path.Combine(folderPath, LocalizationManager.GetXliffFileNameForLanguage(AppId, "es")));
		}

		internal static void AddRandomXliff(string langId, string folderPath)
		{
			var doc = new XLiffDocument { File = {SourceLang = "en", TargetLang=langId}};
			doc.File.HardLineBreakReplacement = LiteralNewline;
			doc.File.Original = "test.dll";
			// only unit
			var tu2 = new TransUnit
			{
				Id = "notUsedId",
				Source = new TransUnitVariant {Lang = "en", Value = "no longer used English text"},
				Target = new TransUnitVariant {Lang = langId, Value = "no longer used Random text"}
			};
			doc.AddTransUnit(tu2);
			doc.Save(Path.Combine(folderPath, LocalizationManager.GetXliffFileNameForLanguage(AppId, langId)));
		}

		/// <summary>
		/// Although it would normally be plausible that AnotherContext.AnotherDialog.Title results from a renaming of
		/// SomeContext.SomeDialog.Title, in the English Xliff we don't allow this. We expect the installed English Xliff
		/// to be up-to-date.
		/// </summary>
		[Test]
		public void OrphanLogicNotUsedForEnglish()
		{
			using (var folder = new TempFolder("OrphanLogicNotUsedForEnglish"))
			{
				MakeEnglishXliffWithApparentOrphan(folder);
				var manager = new LocalizationManager(AppId, AppName, AppVersion,
					GetInstalledDirectory(folder), GetUserModifiedDirectory(folder), GetUserModifiedDirectory(folder));

				LocalizationManager.LoadedManagers[AppId] = manager;

				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "SuperClassMethod.TestId", null, null, "en"), Is.EqualTo("Title"));
				// This will fail if we treated it as an orphan: the ID will not occur at all in the Xliff.
				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "AnotherContext.AnotherDialog.TestId", null, null, "en"), Is.EqualTo("Title"));
			}
		}

		/// <summary>
		/// Make sure the orphan logic IS used where we want it. Arabic is a good test because it will be loaded before the English.
		/// </summary>
		[Test]
		public void OrphanLogicUsedForArabic_ButNotIfFoundInEnglishXliff()
		{
			using (var folder = new TempFolder("OrphanLogicUsedForArabic_ButNotIfFoundInEnglishXliff"))
			{
				MakeEnglishXliffWithApparentOrphan(folder);
				MakeArabicXliffWithApparentOrphans(folder, "Title");

				var manager = new LocalizationManager(AppId, AppName, AppVersion,
					GetInstalledDirectory(folder), GetUserModifiedDirectory(folder), GetUserModifiedDirectory(folder));

				LocalizationManager.LoadedManagers[AppId] = manager;
				LocalizationManager.SetUILanguage("ar", false);

				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "SuperClassMethod.TestId", null, null, "en"), Is.EqualTo("Title"));
				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "AnotherContext.AnotherDialog.TestId", null, null, "en"), Is.EqualTo("Title"));
				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "SuperClassMethod.TestId", null, null, "ar"), Is.EqualTo("Title in Arabic")); // remapped AnObsoleteNameForSuperclass.TestId
				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "AnotherContext.AnotherDialog.TestId", null, null, "ar"), Is.EqualTo("Title in Arabic")); // not remapped, since English Xliff validates it
			}
		}

		[Test]
		public void OrphanLogicNotUsed_WithWrongEnglishSource()
		{
			using (var folder = new TempFolder("OrphanLogicNotUsed_WithWrongEnglishSource"))
			{
				MakeEnglishXliffWithApparentOrphan(folder);
				// The critical difference compared to OrphanLogicUsedForArabic_ButNotIfFoundInEnglishXliff is that the English version of the orphan doesn't match
				MakeArabicXliffWithApparentOrphans(folder, "Some other Title, unrelated to SuperclassMethod.TestId");

				var manager = new LocalizationManager(AppId, AppName, AppVersion,
					GetInstalledDirectory(folder), GetUserModifiedDirectory(folder), GetUserModifiedDirectory(folder));

				LocalizationManager.LoadedManagers[AppId] = manager;
				LocalizationManager.SetUILanguage("ar", false);

				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "SuperClassMethod.TestId", null, null, "en"), Is.EqualTo("Title"));
				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "AnotherContext.AnotherDialog.TestId", null, null, "en"), Is.EqualTo("Title"));
				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "SuperClassMethod.TestId", null, null, "ar"), Is.EqualTo("Title")); // no remapping, English doesn't match, so we have no Arabic, use English
				Assert.That(LocalizationManager.GetDynamicStringOrEnglish(AppId, "AnotherContext.AnotherDialog.TestId", null, null, "ar"), Is.EqualTo("Title in Arabic")); // not remapped, since English Xliff validates it
				// We don't really care what happens to the entry for AnObsoleteNameForSuperclass.TestId, since presumably it is truly obsolete.
			}
		}

		private static void MakeEnglishXliffWithApparentOrphan(TempFolder folder)
		{
			var englishDoc = new XLiffDocument { File = {SourceLang = "en", ProductVersion = AppVersion}};
			englishDoc.File.SetPropValue(LocalizationManager.kAppVersionPropTag, LowerVersion);
			englishDoc.AddTransUnit(MakeTransUnit("en", null, "Title", "SuperClassMethod.TestId", false)); // This is the one ID found in our test code
			englishDoc.AddTransUnit(MakeTransUnit(null, null, "Title", "AnotherContext.AnotherDialog.TestId", true)); // Simulates an 'orphan' that we can't otherwise tell we need.
			Directory.CreateDirectory(GetInstalledDirectory(folder));
			englishDoc.Save(Path.Combine(GetInstalledDirectory(folder), LocalizationManager.GetXliffFileNameForLanguage(AppId, "en")));
		}

		private static void MakeArabicXliffWithApparentOrphans(TempFolder folder, string englishForObsoleteTitle)
		{
			var arabicDoc = new XLiffDocument { File = { SourceLang = "en", TargetLang = "ar" } };
			arabicDoc.File.SetPropValue(LocalizationManager.kAppVersionPropTag, LowerVersion);
			// Note that we do NOT have arabic for SuperClassMethod.TestId. We may end up getting a translation from the orphan, however.
			arabicDoc.AddTransUnit(MakeTransUnit("ar", "Title in Arabic", "Title", "AnotherContext.AnotherDialog.TestId", true)); // Not an orphan, because English Xliff has this too
			// Interpreted as an orphan iff englishForObsoleteTitle is "Title" (matching the English for SuperClassMethod.TestId)
			arabicDoc.AddTransUnit(MakeTransUnit("ar", "Title in Arabic", englishForObsoleteTitle, "AnObsoleteNameForSuperclass.TestId", true));
			Directory.CreateDirectory(GetInstalledDirectory(folder));
			arabicDoc.Save(Path.Combine(GetInstalledDirectory(folder), LocalizationManager.GetXliffFileNameForLanguage(AppId, "ar")));
		}

		static TransUnit MakeTransUnit(string lang, string val, string englishVal, string id, bool dynamic)
		{
			var source = new TransUnitVariant { Lang = "en", Value = englishVal };
			var target = lang == null ? null : new TransUnitVariant { Lang = lang, Value = val };

			var tu = new TransUnit
			{
				Id = id,
			Source = source,
				Target = target
			};
			if (dynamic)
				tu.Dynamic = true;
			return tu;
		}

		[Test]
		public void GetAvailableUILanguageTags_FindsThreeLanguages()
		{
			using (var folder = new TempFolder("GetAvailableUILanguageTags_FindsThreeLanguages"))
			{
				SetupManager(folder, "en");
				var lm = LocalizationManager.LoadedManagers.Values.First();
				var tags = lm.GetAvailableUILanguageTags().ToArray();
				Assert.That(tags.Length, Is.EqualTo(4));
				Assert.That(tags.Contains("ar"), Is.True);
				Assert.That(tags.Contains("en"), Is.True);
				Assert.That(tags.Contains("es"), Is.True);
				Assert.That(tags.Contains("fr"), Is.True);
			}
		}

		[Test]
		public void MergeXliffDocuments_WorksAsExpected()
		{
			var oldDoc = CreateTestXLiffDocument();
			var newDoc = CreateTestXLiffDocument();
			AdjustXliffDocumentForTestingMerge(newDoc);

			var mergedDoc = LocalizationManager.MergeXliffDocuments(newDoc, oldDoc, true);
			Assert.IsNotNull(mergedDoc);
			Assert.That(5, Is.EqualTo(oldDoc.File.Body.TransUnits.Count));
			Assert.That(6, Is.EqualTo(newDoc.File.Body.TransUnits.Count));
			Assert.That(8, Is.EqualTo(mergedDoc.File.Body.TransUnits.Count));

			var tu = mergedDoc.GetTransUnitForId("This.test");
			CheckMergedTransUnit(tu, "This is a test.", new[] {"ID: This.test", "This is only a test"}, false);

			tu = mergedDoc.GetTransUnitForId("That.test");
			CheckMergedTransUnit(tu, "That was a test.", new[] {"ID: That.test", "That is hard to explain, but a literal rendition is okay.", "Not found in static scan of compiled code (version 3.1.4)"}, false);

			tu = mergedDoc.GetTransUnitForId("What.test");
			CheckMergedTransUnit(tu, "What is a good test?", new[] {"ID: What.test", "[OLD NOTE] Whatever you say...", "OLD TEXT (before 3.1.4): What is good test."}, true);

			tu = mergedDoc.GetTransUnitForId("What.this");
			CheckMergedTransUnit(tu, "What is this nonsense?", new[] {"ID: What.this", "Not dynamic: found in static scan of compiled code (version 3.1.4)"}, false);
			Assert.IsNotNull(tu);

			tu = mergedDoc.GetTransUnitForId("This.new1");
			CheckMergedTransUnit(tu, "This is a new test.", new[] {"ID: This.new1", "This is still only a test"}, false);

			tu = mergedDoc.GetTransUnitForId("That.new2");
			CheckMergedTransUnit(tu, "That was an old test.", new[] {"ID: That.new2", "That should be easy to translate."}, false);

			tu = mergedDoc.GetTransUnitForId("What.new3");
			CheckMergedTransUnit(tu, "What's up, doc?", new[] {"ID: What.new3"}, true);

			tu = mergedDoc.GetTransUnitForId("How.now");
			CheckMergedTransUnit(tu, "How now brown cow", new[] {"ID: How.now", "Not found when running compiled program (version 3.1.4)"}, true);
		}

		private void CheckMergedTransUnit(TransUnit tu, string sourceText, string[] notes, bool isDynamic)
		{
			Assert.IsNotNull(tu);
			Assert.That("en", Is.EqualTo(tu.Source.Lang));
			Assert.That(sourceText, Is.EqualTo(tu.Source.Value));
			Assert.That(notes.Length, Is.EqualTo(tu.Notes.Count));
			for (int i = 0; i < notes.Length; ++i)
				Assert.That(notes[i], Is.EqualTo(tu.Notes[i].Text));
			Assert.That(isDynamic, Is.EqualTo(tu.Dynamic));
		}

		private TransUnit CreateTestTransUnit(string id, string source, string[] notes, bool isDynamic)
		{
			var tu = new TransUnit();
			tu.Id = id;
			tu.Source = new TransUnitVariant();
			tu.Source.Lang = "en";
			tu.Source.Value = source;
			tu.AddNote(String.Format("ID: {0}", id));
			for (int i = 0; i < notes.Length; ++i)
				tu.AddNote("en", notes[i]);
			tu.Dynamic = isDynamic;
			return tu;
		}

		private XLiffDocument CreateTestXLiffDocument()
		{
			var doc = new XLiffDocument();
			doc.File.Original = "Testing.dll";
			doc.File.DataType = "plaintext";
			doc.File.ProductVersion = "2.7.1";
			doc.File.SourceLang = "en";
			doc.Version = "1.2";

			var tu1 = CreateTestTransUnit("This.test", "This is a test.", new[] {"This is only a test"}, false);
			doc.AddTransUnit(tu1);
			var tu2 = CreateTestTransUnit("That.test", "That was a test.", new[] {"That is hard to explain, but a literal rendition is okay."}, false);
			doc.AddTransUnit(tu2);
			var tu3 = CreateTestTransUnit("What.test", "What is good test.", new string[] {"Whatever you say..."}, true);
			doc.AddTransUnit(tu3);
			var tu4 = CreateTestTransUnit("What.this", "What is this nonsense?", new string[] {}, true);
			doc.AddTransUnit(tu4);
			var tu5 = CreateTestTransUnit("How.now", "How now brown cow", new string[] {}, true);
			doc.AddTransUnit(tu5);

			return doc;
		}

		private void AdjustXliffDocumentForTestingMerge(XLiffDocument doc)
		{
			doc.File.ProductVersion = "3.1.4";

			// Test for adding new units.
			var tu1 = CreateTestTransUnit("This.new1", "This is a new test.", new[] {"This is still only a test"}, false);
			doc.AddTransUnit(tu1);
			var tu2 = CreateTestTransUnit("That.new2", "That was an old test.", new[] {"That should be easy to translate."}, false);
			doc.AddTransUnit(tu2);
			var tu3 = CreateTestTransUnit("What.new3", "What's up, doc?", new string[] {}, true);
			doc.AddTransUnit(tu3);

			// Test for invalid dynamic setting
			var tu = doc.GetTransUnitForId("What.this");
			tu.Dynamic = false;
			tu.Notes.Clear();
			tu.AddNote("ID: What.this");

			// Test for deleted static unit
			tu = doc.GetTransUnitForId("That.test");
			doc.RemoveTransUnit(tu);

			// Test for modified unit
			tu = doc.GetTransUnitForId("What.test");
			tu.Source.Value = "What is a good test?";
			tu.Notes.Clear();
			tu.AddNote("ID: What.test");

			// Test for deleted (or not covered) dynamic unit
			tu = doc.GetTransUnitForId("How.now");
			doc.RemoveTransUnit(tu);
		}

		[Test]
		public void XLiffWithNewlineReplacement_YieldsStandardizedNewline()
		{
			LocalizationManager.LoadedManagers.Clear();
			using (var folder = new TempFolder("XLiffWithNewlineReplacement_YieldsRealNewline"))
			{
				AddArabicXliff(GetInstalledDirectory(folder));
				AddFrenchXliff(GetInstalledDirectory(folder));
				AddSpanishXliff(GetInstalledDirectory(folder));

				var englishDoc = new XLiffDocument { File = { SourceLang = "en" } };
				englishDoc.File.HardLineBreakReplacement = LiteralNewline;
				englishDoc.File.Original = "test.dll";
				// first unit
				var tu = new TransUnit
				{
					Id = "theId",
					Source = new TransUnitVariant { Lang = "en", Value = "from English\\n Xliff" },
				};
				englishDoc.AddTransUnit(tu);
				englishDoc.Save(Path.Combine(folder.Path, LocalizationManager.GetXliffFileNameForLanguage(AppId, "en")));

				var doc = new XLiffDocument { File = { SourceLang = "en", TargetLang = "fr" } };
				doc.File.Original = "test.dll";
				// first unit
				var tuF = new TransUnit
				{
					Id = "theId",
					Source = new TransUnitVariant { Lang = "en", Value = "from English\\n Xliff" },
					Target = new TransUnitVariant { Lang = "fr", Value = "from French\\n Xliff" },
				};
				tu.Dynamic = true;
				doc.AddTransUnit(tuF);
				doc.Save(Path.Combine(folder.Path, LocalizationManager.GetXliffFileNameForLanguage(AppId, "fr")));

				LocalizationManager.SetUILanguage("fr", true);
				var manager = new LocalizationManager(AppId, AppName, AppVersion,
					GetInstalledDirectory(folder), GetGeneratedDirectory(folder), GetUserModifiedDirectory(folder));
				LocalizationManager.LoadedManagers[AppId] = manager;
				Assert.That(LocalizationManager.GetString("theId", "from English\\n Xliff"), Is.EqualTo("from French" + LocalizedStringCache.kOSRealNewline + " Xliff"));

				// what about one added dynamically, as if discovered in code?
				// We'd like it to correctly handle both the current environment.Newline and a literal \r\n
				// The latter may well fail on Linux though passing on Windows.
				var li = new LocalizingInfo("anotherId") { Text = "Three\r\nlines of" + Environment.NewLine + "French", LangId = "fr" };
				manager.StringCache.UpdateLocalizedInfo(li);
				// The actual stored value, that would be written to the xliff, should have the replacement text in it.
				Assert.That(manager.StringCache.GetValueForExactLangAndId("fr", "anotherId", false), Is.EqualTo("Three\\nlines of\\nFrench"));
				Assert.That(LocalizationManager.GetString("anotherId", "Three\r\nlines of" + Environment.NewLine + "English"), Is.EqualTo("Three" + LocalizedStringCache.kOSRealNewline + "lines of" + LocalizedStringCache.kOSRealNewline + "French"));
			}
		}
	}
}
