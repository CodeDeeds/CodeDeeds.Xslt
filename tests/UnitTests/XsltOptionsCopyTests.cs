using System.Reflection;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that <see cref="XsltOptions.WithOmitXmlDeclaration"/> copies every setting it is not asked to change.
    /// </summary>
    [TestClass]
    public sealed class XsltOptionsCopyTests
    {
        /// <summary>Stands in for any of the resolver interfaces; nothing calls it.</summary>
        public class Stand : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
        }

        /// <summary>A value that differs from the type's default, for a property of any type the options hold.</summary>
        private static object ValueFor(Type type)
        {
            Type under = Nullable.GetUnderlyingType(type) ?? type;

            if (under == typeof(bool))
            {
                return true;
            }

            if (under == typeof(string))
            {
                return "set";
            }

            if (under.IsEnum)
            {
                return Enum.GetValues(under).Cast<object>().Last();
            }

            if (under == typeof(XsltVersion))
            {
                return XsltVersion.V20;
            }

            if (under == typeof(TextWriter))
            {
                return new StringWriter();
            }

            if (under == typeof(System.Xml.Schema.XmlSchemaSet))
            {
                return new System.Xml.Schema.XmlSchemaSet();
            }

            if (under == typeof(IReadOnlyList<object?>))
            {
                return new object?[] { 1 };
            }

            if (under.IsAssignableFrom(typeof(Dictionary<string, object?>)))
            {
                return new Dictionary<string, object?> { ["p"] = 1 };
            }

            if (under.IsInterface)
            {
                return typeof(DispatchProxy)
                    .GetMethods().Single(m => m.Name == nameof(DispatchProxy.Create) && m.GetParameters().Length == 0)
                    .MakeGenericMethod(under, typeof(Stand))
                    .Invoke(null, null)!;
            }

            throw new AssertFailedException($"The test does not know how to make a {under.Name} for an option.");
        }

        /// <summary>
        /// Every public setting, set to something other than its default, comes through the copy untouched
        /// but for the one asked to change. A setting added to the options and left out of the copy fails here
        /// by name.
        /// </summary>
        [TestMethod]
        public void EverySettingSurvivesTheCopy()
        {
            XsltOptions options = new XsltOptions();
            PropertyInfo[] settings = typeof(XsltOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is not null)
                .ToArray();

            Assert.IsGreaterThan(20, settings.Length);

            // XsltOptions has init-only properties, so the values go in through a boxed copy of the object.
            object boxed = options;

            foreach (PropertyInfo property in settings)
            {
                property.SetValue(boxed, ValueFor(property.PropertyType));
            }

            XsltOptions original = (XsltOptions)boxed;
            XsltOptions copy = original.WithOmitXmlDeclaration(false);

            foreach (PropertyInfo property in settings)
            {
                object? expected = property.Name == nameof(XsltOptions.OmitXmlDeclaration)
                    ? false
                    : property.GetValue(original);

                object? actual = property.GetValue(copy);

                // A collection setting may keep a snapshot of what it was given, so it is the content that
                // has to survive and not the instance.
                if (expected is System.Collections.IEnumerable items and not string)
                {
                    CollectionAssert.AreEqual(
                        items.Cast<object?>().ToList(),
                        ((System.Collections.IEnumerable)actual!).Cast<object?>().ToList(),
                        $"{property.Name} was not copied");
                    continue;
                }

                Assert.AreEqual(expected, actual, $"{property.Name} was not copied");
            }
        }
    }
}
