export class Main {
    public static onLoad(executionContext: { getFormContext(): { data: { entity: { getEntityName(): string } } } }): void {
        const formContext = executionContext.getFormContext();
        const entityName = formContext.data.entity.getEntityName();
        const greeting = Main.buildGreeting(entityName);
        console.log(greeting);
    }

    private static buildGreeting(entityName: string): string {
        const parts: string[] = [];
        for (let index = 0; index < 3; index++) {
            parts.push(`${entityName} form loaded (${index})`);
        }
        return parts.join("; ");
    }
}
