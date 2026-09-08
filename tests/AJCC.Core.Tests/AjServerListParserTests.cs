using System.Xml;
using AJCC.Core.Parsers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AjServerListParserTests
{
    [TestMethod]
    public void ParseLinks_ExtractsServerLinks_TrimsAndDeduplicates()
    {
        const string xml = """
            <servers>
              <server link=" ajfsp://server|one.example|9851/ " />
              <server link="AJFSP://SERVER|ONE.EXAMPLE|9851/" />
              <server link="ajfsp://file|ignored.bin|0123456789abcdef0123456789abcdef|1/" />
              <other link="ajfsp://server|ignored.example|9851/" />
              <server link="ajfsp://server|two.example|9852/" />
            </servers>
            """;

        IReadOnlyList<string> links = AjServerListParser.ParseLinks(xml);

        Assert.AreEqual(2, links.Count);
        Assert.AreEqual("ajfsp://server|one.example|9851/", links[0]);
        Assert.AreEqual("ajfsp://server|two.example|9852/", links[1]);
    }

    [TestMethod]
    public void ParseLinks_EmptyInput_ReturnsEmptyList()
    {
        IReadOnlyList<string> links = AjServerListParser.ParseLinks("   ");

        Assert.AreEqual(0, links.Count);
    }

    [TestMethod]
    public void ParseLinks_Dtd_IsRejected()
    {
        const string xml = "<!DOCTYPE servers [<!ENTITY x 'boom'>]><servers><server link='&x;' /></servers>";

        try
        {
            AjServerListParser.ParseLinks(xml);
            Assert.Fail("Expected XmlException for DTD input.");
        }
        catch (XmlException)
        {
            // expected
        }
    }
}
