﻿using JetBrains.FormatRipper.Compound;
using NUnit.Framework;

namespace JetBrains.FormatRipper.Tests
{
  [TestFixture]
  public sealed class MsiUtilTest
  {
    // @formatter:off
    [TestCase(CompoundFile.DigitalSignatureName     , CompoundFile.DigitalSignatureName)]
    [TestCase(CompoundFile.MsiDigitalSignatureExName, CompoundFile.MsiDigitalSignatureExName)]
    [TestCase(CompoundFile.SummaryInformationName   , CompoundFile.SummaryInformationName)]
    [TestCase(CompoundFile.䡀_ColumnsName           , "䡀_Columns")]
    [TestCase(CompoundFile.䡀_StringDataName        , "䡀_StringData")]
    [TestCase(CompoundFile.䡀_StringPoolName        , "䡀_StringPool")]
    [TestCase(CompoundFile.䡀_TablesName            , "䡀_Tables")]
    [TestCase(CompoundFile.䡀_ValidationName        , "䡀_Validation")]
    [TestCase("䡀䑒䗶䏤㾯㼒䔨䈸䆱䠨"                         , "䡀InstallUISequence")]
    [TestCase("䡀䈏䗤䕸㬨䐲䒳䈱䗱䠶"                         , "䡀FeatureComponents")]
    [TestCase("䌋䄱䜵䆾䖸䌷䒦䠱"                           , "Binary.custicon")]
    [TestCase("䌋䄱䜵䅾䑤䕱䐥䠳"                           , "Binary.bannrbmp")]
    [TestCase("䅧䞪䄦䠥"                               , "dbg.cab")]
    // @formatter:on
    [Test]
    public void Test(string name, string expectedName) => Assert.AreEqual(expectedName, MsiUtil.MsiDecodeStreamName(name));
  }
}