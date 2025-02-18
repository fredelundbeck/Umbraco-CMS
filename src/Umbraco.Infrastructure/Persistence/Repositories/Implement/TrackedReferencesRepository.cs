using NPoco;
using Umbraco.Cms.Core.Mapping;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace Umbraco.Cms.Infrastructure.Persistence.Repositories.Implement
{
    internal class TrackedReferencesRepository : ITrackedReferencesRepository
    {
        private readonly IScopeAccessor _scopeAccessor;
        private readonly IUmbracoMapper _umbracoMapper;

        public TrackedReferencesRepository(IScopeAccessor scopeAccessor, IUmbracoMapper umbracoMapper)
        {
            _scopeAccessor = scopeAccessor;
            _umbracoMapper = umbracoMapper;
        }

        /// <summary>
        /// Gets a page of items used in any kind of relation from selected integer ids.
        /// </summary>
        public IEnumerable<RelationItem> GetPagedItemsWithRelations(int[] ids, long pageIndex, int pageSize,
            bool filterMustBeIsDependency, out long totalRecords)
        {
            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();

            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql()
                .SelectDistinct(
                    "[x].[id] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[d].[published] as nodePublished",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.Id, "n", "x")
                .LeftJoin<ContentDto>("c")
                .On<NodeDto, ContentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>(
                    (left, right) => left.ContentTypeId == right.NodeId,
                    aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn")
                .On<ContentTypeDto, NodeDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "ct",
                    aliasRight: "ctn")
                .LeftJoin<DocumentDto>("d")
                .On<NodeDto, DocumentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "d");

            if (ids.Any())
            {
                sql = sql?.Where<NodeDto>(x => ids.Contains(x.NodeId), "n");
            }

            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // Ordering is required for paging
            sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

            Page<RelationItemDto>? pagedResult = _scopeAccessor.AmbientScope?.Database.Page<RelationItemDto>(pageIndex + 1, pageSize, sql);
            totalRecords = Convert.ToInt32(pagedResult?.TotalItems);

            return pagedResult?.Items.Select(MapDtoToEntity) ?? Enumerable.Empty<RelationItem>();
        }

        private Sql<ISqlContext> GetInnerUnionSql()
        {
            if (_scopeAccessor.AmbientScope is null)
            {
                throw new InvalidOperationException("No Ambient Scope available");
            }

            Sql<ISqlContext> innerUnionSqlChild = _scopeAccessor.AmbientScope.Database.SqlContext.Sql().Select(
                    "[cn].uniqueId as [key]", "[pn].uniqueId as otherKey, [cr].childId as id", "[cr].parentId as otherId", "[rt].[alias]", "[rt].[name]",
                    "[rt].[isDependency]", "[rt].[dual]")
                .From<RelationDto>("cr")
                .InnerJoin<RelationTypeDto>("rt")
                .On<RelationDto, RelationTypeDto>((cr, rt) => rt.Dual == false && rt.Id == cr.RelationType, "cr", "rt")
                .InnerJoin<NodeDto>("cn")
                .On<RelationDto, NodeDto>((cr, cn) => cr.ChildId == cn.NodeId, "cr", "cn")
                .InnerJoin<NodeDto>("pn")
                .On<RelationDto, NodeDto>((cr, pn) => cr.ParentId == pn.NodeId, "cr", "pn");

            Sql<ISqlContext> innerUnionSqlDualParent = _scopeAccessor.AmbientScope.Database.SqlContext.Sql().Select(
                    "[pn].uniqueId as [key]", "[cn].uniqueId as otherKey, [dpr].parentId as id", "[dpr].childId as otherId", "[dprt].[alias]", "[dprt].[name]",
                    "[dprt].[isDependency]", "[dprt].[dual]")
                .From<RelationDto>("dpr")
                .InnerJoin<RelationTypeDto>("dprt")
                .On<RelationDto, RelationTypeDto>(
                    (dpr, dprt) => dprt.Dual == true && dprt.Id == dpr.RelationType, "dpr", "dprt")
                .InnerJoin<NodeDto>("cn")
                .On<RelationDto, NodeDto>((dpr, cn) => dpr.ChildId == cn.NodeId, "dpr", "cn")
                .InnerJoin<NodeDto>("pn")
                .On<RelationDto, NodeDto>((dpr, pn) => dpr.ParentId == pn.NodeId, "dpr", "pn");

            Sql<ISqlContext> innerUnionSql3 = _scopeAccessor.AmbientScope.Database.SqlContext.Sql().Select(
                    "[cn].uniqueId as [key]", "[pn].uniqueId as otherKey, [dcr].childId as id", "[dcr].parentId as otherId", "[dcrt].[alias]", "[dcrt].[name]",
                    "[dcrt].[isDependency]", "[dcrt].[dual]")
                .From<RelationDto>("dcr")
                .InnerJoin<RelationTypeDto>("dcrt")
                .On<RelationDto, RelationTypeDto>(
                    (dcr, dcrt) => dcrt.Dual == true && dcrt.Id == dcr.RelationType, "dcr", "dcrt")
                .InnerJoin<NodeDto>("cn")
                .On<RelationDto, NodeDto>((dcr, cn) => dcr.ChildId == cn.NodeId, "dcr", "cn")
                .InnerJoin<NodeDto>("pn")
                .On<RelationDto, NodeDto>((dcr, pn) => dcr.ParentId == pn.NodeId, "dcr", "pn");

            Sql<ISqlContext> innerUnionSql = innerUnionSqlChild.Union(innerUnionSqlDualParent).Union(innerUnionSql3);

            return innerUnionSql;
        }

        /// <summary>
        /// Gets a page of the descending items that have any references, given a parent id.
        /// </summary>
        public IEnumerable<RelationItem> GetPagedDescendantsInReferences(
            int parentId,
            long pageIndex,
            int pageSize,
            bool filterMustBeIsDependency,
            out long totalRecords)
        {
            SqlSyntax.ISqlSyntaxProvider? syntax = _scopeAccessor.AmbientScope?.Database.SqlContext.SqlSyntax;

            // Gets the path of the parent with ",%" added
            Sql<ISqlContext>? subsubQuery = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql()
                .Select(syntax?.GetConcat("[node].[path]", "',%'"))
                .From<NodeDto>("node")
                .Where<NodeDto>(x => x.NodeId == parentId, "node");

            // Gets the descendants of the parent node
            Sql<ISqlContext>? subQuery = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql()
                .Select<NodeDto>(x => x.NodeId)
                .From<NodeDto>()
                .WhereLike<NodeDto>(x => x.Path, subsubQuery);

            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();

            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql().SelectDistinct(
                    "[x].[id] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[d].[published] as nodePublished",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.Id, "n", "x")
                .LeftJoin<ContentDto>("c")
                .On<NodeDto, ContentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>(
                    (left, right) => left.ContentTypeId == right.NodeId,
                    aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn")
                .On<ContentTypeDto, NodeDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "ct",
                    aliasRight: "ctn")
                .LeftJoin<DocumentDto>("d")
                .On<NodeDto, DocumentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "d");

            sql = sql?.WhereIn((System.Linq.Expressions.Expression<Func<NodeDto, object?>>)(x => x.NodeId), subQuery, "n");

            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // Ordering is required for paging
            sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

            Page<RelationItemDto>? pagedResult = _scopeAccessor.AmbientScope?.Database.Page<RelationItemDto>(pageIndex + 1, pageSize, sql);
            totalRecords = Convert.ToInt32(pagedResult?.TotalItems);

            return pagedResult?.Items.Select(MapDtoToEntity) ?? Enumerable.Empty<RelationItem>();
        }


        /// <summary>
        /// Gets a page of items which are in relation with the current item.
        /// Basically, shows the items which depend on the current item.
        /// </summary>
        public IEnumerable<RelationItem> GetPagedRelationsForItem(int id, long pageIndex, int pageSize,
            bool filterMustBeIsDependency, out long totalRecords)
        {
            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();
            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql().SelectDistinct(
                    "[x].[otherId] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[d].[published] as nodePublished",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.OtherId, "n", "x")
                .LeftJoin<ContentDto>("c")
                .On<NodeDto, ContentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>(
                    (left, right) => left.ContentTypeId == right.NodeId,
                    aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn")
                .On<ContentTypeDto, NodeDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "ct",
                    aliasRight: "ctn")
                .LeftJoin<DocumentDto>("d")
                .On<NodeDto, DocumentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "d")
                .Where<UnionHelperDto>(x => x.Id == id, "x");

            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // Ordering is required for paging
            sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

            Page<RelationItemDto>? pagedResult = _scopeAccessor.AmbientScope?.Database.Page<RelationItemDto>(pageIndex + 1, pageSize, sql);
            totalRecords = Convert.ToInt32(pagedResult?.TotalItems);

            return pagedResult?.Items.Select(MapDtoToEntity) ?? Enumerable.Empty<RelationItem>();
        }

        public IEnumerable<RelationItemModel> GetPagedRelationsForItem(
            Guid key,
            long skip,
            long take,
            bool filterMustBeIsDependency,
            out long totalRecords)
        {
            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();
            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql().SelectDistinct(
                    "[x].[otherId] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[d].[published] as nodePublished",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.OtherId, "n", "x")
                .LeftJoin<ContentDto>("c")
                .On<NodeDto, ContentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>(
                    (left, right) => left.ContentTypeId == right.NodeId,
                    aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn")
                .On<ContentTypeDto, NodeDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "ct",
                    aliasRight: "ctn")
                .LeftJoin<DocumentDto>("d")
                .On<NodeDto, DocumentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "d")
                .Where<UnionHelperDto>(x => x.Key == key, "x");


            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // find the count before ordering
            totalRecords = _scopeAccessor.AmbientScope?.Database.Count(sql!) ?? 0;

            RelationItemDto[] pagedResult;

            //Only to all this, if there is items
            if (totalRecords > 0)
            {
                // Ordering is required for paging
                sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

                pagedResult =
                    _scopeAccessor.AmbientScope?.Database.SkipTake<RelationItemDto>(skip, take, sql).ToArray() ??
                    Array.Empty<RelationItemDto>();
            }
            else
            {
                pagedResult = Array.Empty<RelationItemDto>();
            }

            return _umbracoMapper.MapEnumerable<RelationItemDto, RelationItemModel>(pagedResult);
        }

        [Obsolete("Use overload that takes key instead of id. This will be removed in Umbraco 15.")]
        public IEnumerable<RelationItemModel> GetPagedRelationsForItem(
            int id,
            long skip,
            long take,
            bool filterMustBeIsDependency,
            out long totalRecords)
        {
            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();
            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql().SelectDistinct(
                    "[x].[otherId] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.OtherId, "n", "x")
                .LeftJoin<ContentDto>("c").On<NodeDto, ContentDto>(
                (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>(
                (left, right) => left.ContentTypeId == right.NodeId,
                    aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn").On<ContentTypeDto, NodeDto>(
                (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "ct",
                    aliasRight: "ctn")
                .Where<UnionHelperDto>(x => x.Id == id, "x");

            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // find the count before ordering
            totalRecords = _scopeAccessor.AmbientScope?.Database.Count(sql!) ?? 0;

            RelationItemDto[] pagedResult;

            //Only to all this, if there is items
            if (totalRecords > 0)
            {
                // Ordering is required for paging
                sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

                pagedResult =
                    _scopeAccessor.AmbientScope?.Database.SkipTake<RelationItemDto>(skip, take, sql).ToArray() ??
                    Array.Empty<RelationItemDto>();
            }
            else
            {
                pagedResult = Array.Empty<RelationItemDto>();
            }

            return _umbracoMapper.MapEnumerable<RelationItemDto, RelationItemModel>(pagedResult);
        }

        public IEnumerable<RelationItemModel> GetPagedItemsWithRelations(
           ISet<Guid> keys,
           long skip,
           long take,
           bool filterMustBeIsDependency,
           out long totalRecords)
        {
            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();
            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql().SelectDistinct(
                    "[x].[id] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.Id, "n", "x")
                .LeftJoin<ContentDto>("c")
                .On<NodeDto, ContentDto>((left, right) => left.NodeId == right.NodeId, aliasLeft: "n", aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>((left, right) => left.ContentTypeId == right.NodeId, aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn")
                .On<ContentTypeDto, NodeDto>((left, right) => left.NodeId == right.NodeId, aliasLeft: "ct",
                    aliasRight: "ctn");
            if (keys.Any())
            {
                sql = sql?.Where<NodeDto>(x => keys.Contains(x.UniqueId), "n");
            }

            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // find the count before ordering
            totalRecords = _scopeAccessor.AmbientScope?.Database.Count(sql!) ?? 0;

            RelationItemDto[] pagedResult;

            //Only to all this, if there is items
            if (totalRecords > 0)
            {
                // Ordering is required for paging
                sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

                pagedResult =
                    _scopeAccessor.AmbientScope?.Database.SkipTake<RelationItemDto>(skip, take, sql).ToArray() ??
                    Array.Empty<RelationItemDto>();
            }
            else
            {
                pagedResult = Array.Empty<RelationItemDto>();
            }

            return _umbracoMapper.MapEnumerable<RelationItemDto, RelationItemModel>(pagedResult);
        }

        public IEnumerable<RelationItemModel> GetPagedDescendantsInReferences(
            Guid parentKey,
            long skip,
            long take,
            bool filterMustBeIsDependency,
            out long totalRecords)
        {
            SqlSyntax.ISqlSyntaxProvider? syntax = _scopeAccessor.AmbientScope?.Database.SqlContext.SqlSyntax;

            // Gets the path of the parent with ",%" added
            Sql<ISqlContext>? subsubQuery = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql()
                .Select(syntax?.GetConcat("[node].[path]", "',%'"))
                .From<NodeDto>("node")
                .Where<NodeDto>(x => x.UniqueId == parentKey, "node");

            // Gets the descendants of the parent node
            Sql<ISqlContext>? subQuery = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql()
                .Select<NodeDto>(x => x.NodeId)
                .From<NodeDto>()
                .WhereLike<NodeDto>(x => x.Path, subsubQuery);

            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();
            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql().SelectDistinct(
                    "[x].[id] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[d].[published] as nodePublished",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.Id, "n", "x")
                .LeftJoin<ContentDto>("c").On<NodeDto, ContentDto>(
                (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>(
                (left, right) => left.ContentTypeId == right.NodeId,
                    aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn")
                .On<ContentTypeDto, NodeDto>(
                (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "ct",
                    aliasRight: "ctn")
                .LeftJoin<DocumentDto>("d")
                .On<NodeDto, DocumentDto>(
                    (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "d");
            sql = sql?.WhereIn(
                (System.Linq.Expressions.Expression<Func<NodeDto, object?>>)(x => x.NodeId),
                subQuery,
                "n");

            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // find the count before ordering
            totalRecords = _scopeAccessor.AmbientScope?.Database.Count(sql!) ?? 0;

            RelationItemDto[] pagedResult;

            //Only to all this, if there is items
            if (totalRecords > 0)
            {
                // Ordering is required for paging
                sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

                pagedResult =
                    _scopeAccessor.AmbientScope?.Database.SkipTake<RelationItemDto>(skip, take, sql).ToArray() ??
                    Array.Empty<RelationItemDto>();
            }
            else
            {
                pagedResult = Array.Empty<RelationItemDto>();
            }

            return _umbracoMapper.MapEnumerable<RelationItemDto, RelationItemModel>(pagedResult);
        }

        [Obsolete("Use overload that takes keys instead of ids. This will be removed in Umbraco 15.")]
        public IEnumerable<RelationItemModel> GetPagedItemsWithRelations(
           int[] ids,
           long skip,
           long take,
           bool filterMustBeIsDependency,
           out long totalRecords)
        {
            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();
            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql().SelectDistinct(
                    "[x].[id] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.Id, "n", "x")
                .LeftJoin<ContentDto>("c")
                .On<NodeDto, ContentDto>((left, right) => left.NodeId == right.NodeId, aliasLeft: "n", aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>((left, right) => left.ContentTypeId == right.NodeId, aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn")
                .On<ContentTypeDto, NodeDto>((left, right) => left.NodeId == right.NodeId, aliasLeft: "ct",
                    aliasRight: "ctn");
            if (ids.Any())
            {
                sql = sql?.Where<NodeDto>(x => ids.Contains(x.NodeId), "n");
            }

            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // find the count before ordering
            totalRecords = _scopeAccessor.AmbientScope?.Database.Count(sql!) ?? 0;

            RelationItemDto[] pagedResult;

            //Only to all this, if there is items
            if (totalRecords > 0)
            {
                // Ordering is required for paging
                sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

                pagedResult =
                    _scopeAccessor.AmbientScope?.Database.SkipTake<RelationItemDto>(skip, take, sql).ToArray() ??
                    Array.Empty<RelationItemDto>();
            }
            else
            {
                pagedResult = Array.Empty<RelationItemDto>();
            }

            return _umbracoMapper.MapEnumerable<RelationItemDto, RelationItemModel>(pagedResult);
        }

        public IEnumerable<RelationItemModel> GetPagedDescendantsInReferences(
            int parentId,
            long skip,
            long take,
            bool filterMustBeIsDependency,
            out long totalRecords)
        {
            SqlSyntax.ISqlSyntaxProvider? syntax = _scopeAccessor.AmbientScope?.Database.SqlContext.SqlSyntax;

            // Gets the path of the parent with ",%" added
            Sql<ISqlContext>? subsubQuery = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql()
                .Select(syntax?.GetConcat("[node].[path]", "',%'"))
                .From<NodeDto>("node")
                .Where<NodeDto>(x => x.NodeId == parentId, "node");

            // Gets the descendants of the parent node
            Sql<ISqlContext>? subQuery = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql()
                .Select<NodeDto>(x => x.NodeId)
                .From<NodeDto>()
                .WhereLike<NodeDto>(x => x.Path, subsubQuery);

            Sql<ISqlContext> innerUnionSql = GetInnerUnionSql();
            Sql<ISqlContext>? sql = _scopeAccessor.AmbientScope?.Database.SqlContext.Sql().SelectDistinct(
                    "[x].[id] as nodeId",
                    "[n].[uniqueId] as nodeKey",
                    "[n].[text] as nodeName",
                    "[n].[nodeObjectType] as nodeObjectType",
                    "[ct].[icon] as contentTypeIcon",
                    "[ct].[alias] as contentTypeAlias",
                    "[ctn].[text] as contentTypeName",
                    "[x].[alias] as relationTypeAlias",
                    "[x].[name] as relationTypeName",
                    "[x].[isDependency] as relationTypeIsDependency",
                    "[x].[dual] as relationTypeIsBidirectional")
                .From<NodeDto>("n")
                .InnerJoinNested(innerUnionSql, "x")
                .On<NodeDto, UnionHelperDto>((n, x) => n.NodeId == x.Id, "n", "x")
                .LeftJoin<ContentDto>("c").On<NodeDto, ContentDto>(
                (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "n",
                    aliasRight: "c")
                .LeftJoin<ContentTypeDto>("ct")
                .On<ContentDto, ContentTypeDto>(
                (left, right) => left.ContentTypeId == right.NodeId,
                    aliasLeft: "c",
                    aliasRight: "ct")
                .LeftJoin<NodeDto>("ctn")
                .On<ContentTypeDto, NodeDto>(
                (left, right) => left.NodeId == right.NodeId,
                    aliasLeft: "ct",
                    aliasRight: "ctn");
            sql = sql?.WhereIn(
                (System.Linq.Expressions.Expression<Func<NodeDto, object?>>)(x => x.NodeId),
                subQuery,
                "n");

            if (filterMustBeIsDependency)
            {
                sql = sql?.Where<RelationTypeDto>(rt => rt.IsDependency, "x");
            }

            // find the count before ordering
            totalRecords = _scopeAccessor.AmbientScope?.Database.Count(sql!) ?? 0;

            RelationItemDto[] pagedResult;

            //Only to all this, if there is items
            if (totalRecords > 0)
            {
                // Ordering is required for paging
                sql = sql?.OrderBy<RelationTypeDto>(x => x.Alias, "x");

                pagedResult =
                    _scopeAccessor.AmbientScope?.Database.SkipTake<RelationItemDto>(skip, take, sql).ToArray() ??
                    Array.Empty<RelationItemDto>();
            }
            else
            {
                pagedResult = Array.Empty<RelationItemDto>();
            }

            return _umbracoMapper.MapEnumerable<RelationItemDto, RelationItemModel>(pagedResult);
        }

        private class UnionHelperDto
        {
            [Column("id")] public int Id { get; set; }

            [Column("otherId")] public int OtherId { get; set; }

            [Column("key")] public Guid Key { get; set; }

            [Column("otherKey")] public Guid OtherKey { get; set; }

            [Column("alias")] public string? Alias { get; set; }

            [Column("name")] public string? Name { get; set; }

            [Column("isDependency")] public bool IsDependency { get; set; }

            [Column("dual")] public bool Dual { get; set; }
        }

        private RelationItem MapDtoToEntity(RelationItemDto dto)
        {
            return new RelationItem()
            {
                NodeId = dto.ChildNodeId,
                NodeKey = dto.ChildNodeKey,
                NodeType = ObjectTypes.GetUdiType(dto.ChildNodeObjectType),
                NodeName = dto.ChildNodeName,
                NodePublished = dto.ChildNodePublished,
                RelationTypeName = dto.RelationTypeName,
                RelationTypeIsBidirectional = dto.RelationTypeIsBidirectional,
                RelationTypeIsDependency = dto.RelationTypeIsDependency,
                ContentTypeAlias = dto.ChildContentTypeAlias,
                ContentTypeIcon = dto.ChildContentTypeIcon,
                ContentTypeName = dto.ChildContentTypeName,
            };
        }
    }
}
