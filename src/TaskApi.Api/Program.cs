using Microsoft.EntityFrameworkCore;
using TaskApi.Api.Data;
using TaskApi.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=todos.db"));

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/todos", async (AppDbContext db) =>
{
    var todos = await db.TodoItems.ToListAsync();
    return todos;
})
.WithName("GetAllTodos");

app.MapGet("/todos/{id}", async (int id, AppDbContext db) =>
{
    var todo = await db.TodoItems.FindAsync(id);
    return todo is not null ? Results.Ok(todo) : Results.NotFound();
})
.WithName("GetTodoById");

app.MapPost("/todos", async (TodoItem newTodo, AppDbContext db) =>
{
    db.TodoItems.Add(newTodo);
    await db.SaveChangesAsync();
    return Results.Created($"/todos/{newTodo.Id}", newTodo);
})
.WithName("CreateTodo");

app.Run();
