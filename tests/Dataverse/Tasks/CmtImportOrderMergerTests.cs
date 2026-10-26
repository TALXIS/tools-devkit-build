using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TALXIS.DevKit.Build.Dataverse.Tasks.Tests
{
    /// <summary>
    /// Each package's entityImportOrder is a set of "comes before" rules; the merged order must break none of them.
    /// </summary>
    public class CmtImportOrderMergerTests
    {
        [Fact]
        public void Entity_listed_first_by_a_later_package_moves_ahead_of_the_entities_it_must_precede()
        {
            var errors = new List<string>();

            var merged = CmtImportOrderMerger.Merge(new[]
            {
                Order("a/data_schema.xml", "contact", "annotation"),
                Order("b/data_schema.xml", "account", "contact"),
            }, errors);

            Assert.Empty(errors);
            Assert.Equal(new[] { "account", "contact", "annotation" }, merged);
        }

        [Fact]
        public void Single_list_is_kept_as_it_is()
        {
            var errors = new List<string>();

            var merged = CmtImportOrderMerger.Merge(new[] { Order("a/data_schema.xml", "c", "a", "b") }, errors);

            Assert.Empty(errors);
            Assert.Equal(new[] { "c", "a", "b" }, merged);
        }

        [Fact]
        public void Lists_without_shared_entities_follow_one_another()
        {
            var errors = new List<string>();

            var merged = CmtImportOrderMerger.Merge(new[]
            {
                Order("a/data_schema.xml", "a", "b"),
                Order("b/data_schema.xml", "c", "d"),
            }, errors);

            Assert.Empty(errors);
            Assert.Equal(new[] { "a", "b", "c", "d" }, merged);
        }

        [Fact]
        public void Entity_repeated_within_one_list_is_not_a_conflict()
        {
            var errors = new List<string>();

            var merged = CmtImportOrderMerger.Merge(new[] { Order("a/data_schema.xml", "a", "b", "a", "c") }, errors);

            Assert.Empty(errors);
            Assert.Equal(new[] { "a", "b", "c" }, merged);
        }

        [Fact]
        public void Contradicting_lists_are_reported_with_both_rules_and_their_files()
        {
            var errors = new List<string>();

            var merged = CmtImportOrderMerger.Merge(new[]
            {
                Order("a/data_schema.xml", "account", "contact"),
                Order("b/data_schema.xml", "contact", "account"),
            }, errors);

            Assert.Empty(merged);
            var error = Assert.Single(errors);
            Assert.Contains("'account' before 'contact' in a/data_schema.xml", error);
            Assert.Contains("'contact' before 'account' in b/data_schema.xml", error);
        }

        [Fact]
        public void Contradiction_spread_over_three_lists_is_reported()
        {
            var errors = new List<string>();

            var merged = CmtImportOrderMerger.Merge(new[]
            {
                Order("a/data_schema.xml", "x", "y"),
                Order("b/data_schema.xml", "y", "z"),
                Order("c/data_schema.xml", "z", "x"),
            }, errors);

            Assert.Empty(merged);
            var error = Assert.Single(errors);
            Assert.Contains("'x' before 'y' in a/data_schema.xml", error);
            Assert.Contains("'y' before 'z' in b/data_schema.xml", error);
            Assert.Contains("'z' before 'x' in c/data_schema.xml", error);
        }

        [Fact]
        public void Where_the_lists_leave_the_order_open_an_entity_goes_after_the_entities_it_looks_up()
        {
            var errors = new List<string>();

            var merged = CmtImportOrderMerger.Merge(new[]
            {
                Order("products/data_schema.xml", "product", "pricelistitem"),
                Order("main/data_schema.xml", "currency", "pricelist"),
            }, errors, Lookups(("product", "currency")));

            Assert.Empty(errors);
            Assert.Equal(new[] { "currency", "product", "pricelistitem", "pricelist" }, merged);
        }

        [Fact]
        public void List_still_wins_over_a_lookup()
        {
            var errors = new List<string>();

            var merged = CmtImportOrderMerger.Merge(new[] { Order("a/data_schema.xml", "product", "currency") }, errors, Lookups(("product", "currency")));

            Assert.Empty(errors);
            Assert.Equal(new[] { "product", "currency" }, merged);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> Lookups(params (string From, string To)[] lookups) =>
            lookups.GroupBy(l => l.From).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(l => l.To).ToList());

        private static (string Source, IReadOnlyList<string> Order) Order(string source, params string[] names) => (source, names);
    }
}
