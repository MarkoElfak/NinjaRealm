using NinjaRealm.Core.Content;
using NUnit.Framework;

namespace NinjaRealm.Tests
{
    public class ContentIdTests
    {
        [TestCase("ninja.kaito")]
        [TestCase("weapon.shadow_katana")]
        [TestCase("trait.element.fire")]
        [TestCase("resource.gold2")]
        public void IsValid_AcceptsLowercaseCategoryAndName(string id)
        {
            Assert.IsTrue(ContentId.IsValid(id));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("ninja")]
        [TestCase("Ninja.kaito")]
        [TestCase("ninja.Kaito")]
        [TestCase("ninja..kaito")]
        [TestCase("ninja.kaito ")]
        [TestCase("ninja.kaito_")]
        [TestCase("ninja.kai-to")]
        [TestCase(".kaito")]
        [TestCase("1ninja.kaito")]
        public void IsValid_RejectsMalformedIds(string id)
        {
            Assert.IsFalse(ContentId.IsValid(id));
        }

        [Test]
        public void Category_ReturnsPartBeforeFirstDot()
        {
            Assert.AreEqual("trait", ContentId.Category("trait.element.fire"));
        }

        [Test]
        public void Category_ReturnsNullForInvalidId()
        {
            Assert.IsNull(ContentId.Category("Kaito"));
        }
    }
}
