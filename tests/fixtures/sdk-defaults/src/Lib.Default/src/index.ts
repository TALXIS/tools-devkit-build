export class TransactionForm {
    public static onLoad(executionContext: { getFormContext(): { data: { entity: { getEntityName(): string } } } }): void {
        const entityName = executionContext.getFormContext().data.entity.getEntityName();
        const parts: string[] = [];
        for (let index = 0; index < 3; index++) {
            parts.push(`${entityName} loaded (${index})`);
        }
        console.log(parts.join("; "));
    }
}
