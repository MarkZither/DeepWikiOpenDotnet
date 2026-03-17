using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DeepWiki.ApiService.Controllers;
using DeepWiki.ApiService.Models;
using DeepWiki.Data.Abstractions.Entities;
using DeepWiki.Rag.Core.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DeepWiki.ApiService.Tests.Controllers;

public class WikiControllerTests
{
    // ── Inline test double ──────────────────────────────────────────────────

    private sealed class FakeWikiService : IWikiService
    {
        public WikiEntity? WikiToReturn { get; set; }
        public List<WikiEntity> WikiList { get; set; } = [];
        public WikiPageEntity? PageToReturn { get; set; }
        public bool ShouldThrowNotFound { get; set; } = false;
        public bool DeleteResult { get; set; } = true;

        public Task<WikiEntity> CreateWikiAsync(string name, string collectionId, string? description,
            IEnumerable<WikiPageEntity>? pages = null, CancellationToken ct = default)
        {
            var wiki = new WikiEntity
            {
                Id = Guid.NewGuid(),
                Name = name,
                CollectionId = collectionId,
                Description = description,
                Status = WikiStatus.Complete,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Pages = pages?.ToList() ?? []
            };
            return Task.FromResult(wiki);
        }

        public Task<WikiEntity?> GetWikiByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(WikiToReturn);

        public Task<(IReadOnlyList<WikiEntity> Items, int TotalCount)> GetProjectsAsync(int page, int pageSize, CancellationToken ct = default)
        {
            IReadOnlyList<WikiEntity> items = WikiList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult((items, WikiList.Count));
        }

        public Task<bool> DeleteWikiAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(DeleteResult);

        public Task<WikiPageEntity?> GetPageByIdAsync(Guid wikiId, Guid pageId, CancellationToken ct = default)
            => Task.FromResult(PageToReturn);

        public Task<IReadOnlyList<WikiPageEntity>> GetRelatedPagesAsync(Guid pageId, CancellationToken ct = default)
        {
            IReadOnlyList<WikiPageEntity> empty = [];
            return Task.FromResult(empty);
        }

        public Task<WikiEntity?> UpdateWikiDescriptionAsync(Guid id, string? description, CancellationToken ct = default)
        {
            if (WikiToReturn == null) return Task.FromResult<WikiEntity?>(null);
            WikiToReturn.Description = description;
            return Task.FromResult<WikiEntity?>(WikiToReturn);
        }

        public Task<WikiPageEntity> AddPageAsync(Guid wikiId, string title, string content, string sectionPath,
            int sortOrder, Guid? parentPageId, IEnumerable<Guid>? relatedPageIds = null, CancellationToken ct = default)
        {
            var page = new WikiPageEntity
            {
                Id = Guid.NewGuid(),
                WikiId = wikiId,
                Title = title,
                Content = content,
                SectionPath = sectionPath,
                SortOrder = sortOrder,
                Status = PageStatus.OK,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            return Task.FromResult(page);
        }

        public Task<WikiPageEntity?> UpdatePageAsync(Guid wikiId, Guid pageId, string? title, string? content,
            string? sectionPath, int? sortOrder, IEnumerable<Guid>? relatedPageIds, CancellationToken ct = default)
            => Task.FromResult(PageToReturn);

        public Task<bool> DeletePageAsync(Guid wikiId, Guid pageId, CancellationToken ct = default)
            => Task.FromResult(DeleteResult);

        public Task UpdateRelatedPagesAsync(Guid wikiId, Guid pageId, IEnumerable<Guid> relatedPageIds, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static WikiController CreateController(IWikiService? svc = null, IWikiGenerationService? generationSvc = null)
    {
        // Provide a stub IServiceScopeFactory so the 202 generate endpoint can create a scope.
        var scopeFactory = new StubServiceScopeFactory(svc, generationSvc);
        var controller = new WikiController(svc ?? new FakeWikiService(), generationSvc ?? new StubWikiGenerationService(), scopeFactory);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static WikiEntity MakeWiki(string name = "Test Wiki") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CollectionId = "col-001",
        Status = WikiStatus.Complete,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Pages = []
    };

    private static WikiPageEntity MakePage(Guid wikiId) => new()
    {
        Id = Guid.NewGuid(),
        WikiId = wikiId,
        Title = "Page Title",
        Content = "Content",
        SectionPath = "Section",
        SortOrder = 0,
        Status = PageStatus.OK,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // ── POST /api/wiki (201 + 400) ────────────────────────────────────────────

    [Fact]
    public async Task CreateWiki_ValidRequest_Returns201WithWikiResponse()
    {
        var controller = CreateController();
        var request = new CreateWikiRequest { Name = "My Wiki", CollectionId = "col-001" };

        var result = await controller.CreateWiki(request);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        var body = created.Value.Should().BeOfType<WikiResponse>().Subject;
        body.Name.Should().Be("My Wiki");
    }

    [Fact]
    public async Task CreateWiki_InvalidModel_Returns400()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError("Name", "Name is required");
        var request = new CreateWikiRequest { Name = "", CollectionId = "col-001" };

        var result = await controller.CreateWiki(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── GET /api/wiki/{id} (200 + 404) ────────────────────────────────────────

    [Fact]
    public async Task GetWiki_ExistingId_Returns200WithWikiResponse()
    {
        var wiki = MakeWiki();
        var svc = new FakeWikiService { WikiToReturn = wiki };
        var controller = CreateController(svc);

        var result = await controller.GetWiki(wiki.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<WikiResponse>();
    }

    [Fact]
    public async Task GetWiki_NonExistentId_Returns404()
    {
        var svc = new FakeWikiService { WikiToReturn = null };
        var controller = CreateController(svc);

        var result = await controller.GetWiki(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ── DELETE /api/wiki/{id} (204 + 404) ─────────────────────────────────────

    [Fact]
    public async Task DeleteWiki_ExistingId_Returns204()
    {
        var svc = new FakeWikiService { DeleteResult = true };
        var controller = CreateController(svc);

        var result = await controller.DeleteWiki(Guid.NewGuid());

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteWiki_NonExistentId_Returns404()
    {
        var svc = new FakeWikiService { DeleteResult = false };
        var controller = CreateController(svc);

        var result = await controller.DeleteWiki(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── GET /api/wiki/projects (200 + pagination) ─────────────────────────────

    [Fact]
    public async Task GetProjects_Returns200WithPagedResult()
    {
        var svc = new FakeWikiService
        {
            WikiList = [MakeWiki("A"), MakeWiki("B"), MakeWiki("C")]
        };
        var controller = CreateController(svc);

        var result = await controller.GetProjects(page: 1, pageSize: 2);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<PagedResult<WikiSummaryResponse>>().Subject;
        body.Items.Should().HaveCount(2);
        body.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetProjects_EmptyList_Returns200WithEmptyItems()
    {
        var controller = CreateController();

        var result = await controller.GetProjects(1, 20);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<PagedResult<WikiSummaryResponse>>().Subject;
        body.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
    }

    // ── PUT /api/wiki/{id} (200 + 400 + 404) ─────────────────────────────────

    [Fact]
    public async Task UpdateWikiDescription_ValidRequest_Returns200()
    {
        var wiki = MakeWiki();
        var svc = new FakeWikiService { WikiToReturn = wiki };
        var controller = CreateController(svc);
        var request = new UpdateWikiRequest { Description = "New description" };

        var result = await controller.UpdateWiki(wiki.Id, request);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UpdateWikiDescription_WikiNotFound_Returns404()
    {
        var svc = new FakeWikiService { WikiToReturn = null };
        var controller = CreateController(svc);

        var result = await controller.UpdateWiki(Guid.NewGuid(), new UpdateWikiRequest { Description = "desc" });

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ── GET /api/wiki/{id}/pages/{pageId} (200 + 404) ────────────────────────

    [Fact]
    public async Task GetPage_ExistingPage_Returns200WithPageResponse()
    {
        var wiki = MakeWiki();
        var page = MakePage(wiki.Id);
        var svc = new FakeWikiService { WikiToReturn = wiki, PageToReturn = page };
        var controller = CreateController(svc);

        var result = await controller.GetPage(wiki.Id, page.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<WikiPageResponse>();
    }

    [Fact]
    public async Task GetPage_NonExistentPage_Returns404()
    {
        var wiki = MakeWiki();
        var svc = new FakeWikiService { WikiToReturn = wiki, PageToReturn = null };
        var controller = CreateController(svc);

        var result = await controller.GetPage(wiki.Id, Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ── PUT /api/wiki/{id}/pages/{pageId} (200 + 404) ────────────────────────

    [Fact]
    public async Task UpdatePage_ExistingPage_Returns200()
    {
        var wiki = MakeWiki();
        var page = MakePage(wiki.Id);
        var svc = new FakeWikiService { WikiToReturn = wiki, PageToReturn = page };
        var controller = CreateController(svc);
        var request = new UpdateWikiPageRequest { Title = "New Title" };

        var result = await controller.UpdatePage(wiki.Id, page.Id, request);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UpdatePage_NonExistentPage_Returns404()
    {
        var wiki = MakeWiki();
        var svc = new FakeWikiService { WikiToReturn = wiki, PageToReturn = null };
        var controller = CreateController(svc);

        var result = await controller.UpdatePage(wiki.Id, Guid.NewGuid(), new UpdateWikiPageRequest());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ── POST /api/wiki/{id}/pages (201 + 400) ────────────────────────────────

    [Fact]
    public async Task AddPage_ValidRequest_Returns201()
    {
        var wiki = MakeWiki();
        var svc = new FakeWikiService { WikiToReturn = wiki };
        var controller = CreateController(svc);
        var request = new CreateWikiPageRequest { Title = "New Page", Content = "Content", SectionPath = "Section" };

        var result = await controller.AddPage(wiki.Id, request);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        created.Value.Should().BeOfType<WikiPageResponse>();
    }

    [Fact]
    public async Task AddPage_WikiNotFound_Returns404()
    {
        var svc = new FakeWikiService { WikiToReturn = null };
        var controller = CreateController(svc);
        var request = new CreateWikiPageRequest { Title = "Page", Content = "Content", SectionPath = "Section" };

        var result = await controller.AddPage(Guid.NewGuid(), request);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task AddPage_InvalidModel_Returns400()
    {
        var wiki = MakeWiki();
        var svc = new FakeWikiService { WikiToReturn = wiki };
        var controller = CreateController(svc);
        controller.ModelState.AddModelError("Title", "Title is required");

        var result = await controller.AddPage(wiki.Id, new CreateWikiPageRequest());

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── DELETE /api/wiki/{id}/pages/{pageId} (204 + 404) ─────────────────────

    [Fact]
    public async Task DeletePage_ExistingPage_Returns204()
    {
        var wiki = MakeWiki();
        var svc = new FakeWikiService { WikiToReturn = wiki, DeleteResult = true };
        var controller = CreateController(svc);

        var result = await controller.DeletePage(wiki.Id, Guid.NewGuid());

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeletePage_NonExistentPage_Returns404()
    {
        var wiki = MakeWiki();
        var svc = new FakeWikiService { WikiToReturn = wiki, DeleteResult = false };
        var controller = CreateController(svc);

        var result = await controller.DeletePage(wiki.Id, Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    private sealed class StubWikiGenerationService : IWikiGenerationService
    {
        public async IAsyncEnumerable<DeepWiki.Rag.Core.Models.WikiGenerationProgress> GenerateAsync(
            DeepWiki.Rag.Core.Models.WikiGenerationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
        {
            await System.Threading.Tasks.Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>
    /// Minimal <see cref="IServiceScopeFactory"/> stub that resolves the registered
    /// wiki services from a pre-built <see cref="ServiceProvider"/> so that the
    /// 202 fire-and-forget generate endpoint can create a scope in tests.
    /// </summary>
    private sealed class StubServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IWikiService _wikiService;
        private readonly IWikiGenerationService _generationService;

        public StubServiceScopeFactory(IWikiService? wikiService, IWikiGenerationService? generationService)
        {
            _wikiService = wikiService ?? new FakeWikiService();
            _generationService = generationService ?? new StubWikiGenerationService();
        }

        public IServiceScope CreateScope()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton(_wikiService);
            services.AddSingleton(_generationService);
            services.AddSingleton<IWikiGenerationService>(_generationService);
            var provider = services.BuildServiceProvider();
            return provider.CreateScope();
        }
    }
}
