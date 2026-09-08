using DesktopTodoWidget.Data;
using Xunit;

namespace DesktopTodoWidget.Tests;

public sealed class TodoListViewModelTests
{
    [Fact]
    public void Add_validText_appendsItemToTheEnd()
    {
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second")]);

        var addedItem = viewModel.Add("Third");

        Assert.NotNull(addedItem);
        Assert.Equal(["First", "Second", "Third"], viewModel.Items.Select(item => item.Text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_emptyOrWhitespaceText_doesNotAddAnItem(string text)
    {
        var viewModel = new TodoListViewModel();

        var addedItem = viewModel.Add(text);

        Assert.Null(addedItem);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public void Add_trimsLeadingAndTrailingWhitespace()
    {
        var viewModel = new TodoListViewModel();

        var addedItem = viewModel.Add("  Buy milk  ");

        Assert.Equal("Buy milk", addedItem!.Text);
    }

    [Fact]
    public void Add_longText_truncatesItTo500Characters()
    {
        var viewModel = new TodoListViewModel();

        var addedItem = viewModel.Add(new string('x', 501));

        Assert.Equal(500, addedItem!.Text.Length);
    }

    [Fact]
    public void Add_duplicateText_keepsBothItems()
    {
        var viewModel = new TodoListViewModel();

        viewModel.Add("Same task");
        viewModel.Add("Same task");

        Assert.Equal(["Same task", "Same task"], viewModel.Items.Select(item => item.Text));
    }

    [Fact]
    public void ToggleDone_twice_restoresTheOriginalState()
    {
        var viewModel = new TodoListViewModel([CreateItem("Task")]);
        var item = Assert.Single(viewModel.Items);

        viewModel.ToggleDone(item);
        viewModel.ToggleDone(item);

        Assert.False(item.IsDone);
    }

    [Fact]
    public void Remove_removesOnlyTheRequestedItemAndPreservesOtherOrder()
    {
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second"), CreateItem("Third")]);
        var itemToRemove = viewModel.Items[1];

        var wasRemoved = viewModel.Remove(itemToRemove);

        Assert.True(wasRemoved);
        Assert.Equal(["First", "Third"], viewModel.Items.Select(item => item.Text));
    }

    [Fact]
    public void Rename_validText_updatesTheItem()
    {
        var viewModel = new TodoListViewModel([CreateItem("Before")]);
        var item = Assert.Single(viewModel.Items);

        var wasRenamed = viewModel.Rename(item, "  After  ");

        Assert.True(wasRenamed);
        Assert.Equal("After", item.Text);
    }

    [Fact]
    public void Rename_emptyText_keepsTheOriginalText()
    {
        var viewModel = new TodoListViewModel([CreateItem("Keep me")]);
        var item = Assert.Single(viewModel.Items);

        var wasRenamed = viewModel.Rename(item, "  ");

        Assert.False(wasRenamed);
        Assert.Equal("Keep me", item.Text);
    }

    [Fact]
    public void ToggleDone_keepsTheItemAtItsOriginalPosition()
    {
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second"), CreateItem("Third")]);
        var originalOrder = viewModel.Items.Select(item => item.Id).ToArray();

        viewModel.ToggleDone(viewModel.Items[1]);

        Assert.True(viewModel.Items[1].IsDone);
        Assert.Equal(originalOrder, viewModel.Items.Select(item => item.Id));
    }

    [Fact]
    public void Move_forward_usesACollectionMoveAndPreservesTheRemainingOrder()
    {
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second"), CreateItem("Third")]);
        var collectionChanges = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        viewModel.Items.CollectionChanged += (_, eventArgs) => collectionChanges.Add(eventArgs);

        viewModel.Move(0, 1);

        Assert.Equal(["Second", "First", "Third"], viewModel.Items.Select(item => item.Text));
        Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Move, Assert.Single(collectionChanges).Action);
    }

    [Fact]
    public void Move_firstItemToLast_handlesTheCollectionBoundary()
    {
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second"), CreateItem("Third")]);

        viewModel.Move(0, 2);

        Assert.Equal(["Second", "Third", "First"], viewModel.Items.Select(item => item.Text));
    }

    [Fact]
    public void Move_backward_preservesTheRemainingOrder()
    {
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second"), CreateItem("Third")]);

        viewModel.Move(2, 0);

        Assert.Equal(["Third", "First", "Second"], viewModel.Items.Select(item => item.Text));
    }

    [Fact]
    public void Move_sameIndex_keepsTheCollectionAndDoesNotNotifyPersistence()
    {
        var notificationCount = 0;
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second")], () => notificationCount++);

        viewModel.Move(1, 1);

        Assert.Equal(["First", "Second"], viewModel.Items.Select(item => item.Text));
        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public void Move_outOfRangeIndices_areIgnoredWithoutThrowingOrNotifyingPersistence()
    {
        var notificationCount = 0;
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second")], () => notificationCount++);

        var exception = Record.Exception(() =>
        {
            viewModel.Move(-1, 0);
            viewModel.Move(0, -1);
            viewModel.Move(2, 0);
            viewModel.Move(0, 2);
        });

        Assert.Null(exception);
        Assert.Equal(["First", "Second"], viewModel.Items.Select(item => item.Text));
        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public void Move_completedItem_allowsItToMoveWithoutChangingItsCompletionState()
    {
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Done", isDone: true), CreateItem("Third")]);

        viewModel.Move(1, 0);

        Assert.Equal(["Done", "First", "Third"], viewModel.Items.Select(item => item.Text));
        Assert.True(viewModel.Items[0].IsDone);
    }

    [Fact]
    public void Move_validIndices_notifyTheInjectedPersistenceCallback()
    {
        var notificationCount = 0;
        var viewModel = new TodoListViewModel([CreateItem("First"), CreateItem("Second")], () => notificationCount++);

        viewModel.Move(0, 1);

        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public void Mutations_notifyTheInjectedPersistenceCallback()
    {
        var notificationCount = 0;
        var viewModel = new TodoListViewModel([CreateItem("Existing")], () => notificationCount++);

        var addedItem = viewModel.Add("Added");
        viewModel.ToggleDone(addedItem!);
        viewModel.Rename(addedItem!, "Renamed");
        viewModel.Remove(addedItem!);

        Assert.Equal(4, notificationCount);
    }

    private static TodoItem CreateItem(string text, bool isDone = false)
    {
        return new TodoItem
        {
            Id = Guid.NewGuid(),
            Text = text,
            IsDone = isDone,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }
}
