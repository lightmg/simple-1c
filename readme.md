#Simple1C

*This project is licensed under the terms of the MIT license.*

Несмотря на то, что еще в 2013-м году 1С опубликовал REST апи
поверх протокола OData ([раз](https://wonderland.v8.1c.ru/blog/avtomaticheskiy-rest-interfeys-prikladnykh-resheniy),
[два](https://wonderland.v8.1c.ru/blog/rasshirenie-podderzhki-protokola-odata)),
многим и сегодня по разным причинам приходится использовать для интеграции
старые COM-интерфейсы. Мы упрощаем использование этих интерфейсов
за счет автоматической генерации классов по метаданным 1С
и реализации [LINQ-провайдера](https://msdn.microsoft.com/en-us/library/bb308959.aspx).
Используем C# как основной язык для автогенерации.
Механику провайдера упрощаем за счет [вот этой](https://relinq.codeplex.com) классной штуки.
Аналогичные проекты: http://www.linq-demo.1csoftware.com, http://www.vanessa-sharp.ru/reference-linq.html.

##Основные возможности
* Автоматическая генерация классов объектной модели по 1С-конфигурации :
```
Generator.exe
	-connectionString File=C:\my-1c-db;Usr=Администратор;Pwd=
	-namespaceRoot MyNameSpace.ObjectModel1C
	-scanItems Документ.СписаниеСРасчетногоСчета
	-sourcePath C:\sources\AwesomeSolution\ObjectModel1CProject\AutoGenerated
```
Такая команда создаст класс СписаниеСРасчетногоСчета в папке Документы.
Этот класс будет обладать всеми свойствами исходного 1С-документа
СписаниеСРасчетногоСчета, включая все табличные части. По классу так же будет создано
для каждого из объектов конфигурации, на которые эти свойства
ссылаются (транзитивное замыкание).

* Для имен генерируемых классов используем исходные русскоязычные идентификаторы из 1С.
За счет этого модель становится проще - нет необходимости придумывать англоязычные
аналоги для весьма специфических терминов 1С, одни и те же вещи называются одинаково
по всей кодовой базе.

* Не навязываем механизм управления коннекциями. Здесь возможны различные варианты
(ThreadLocal, Pool, пересоздание по таймауту, пересоздание при обрыве и т.п.),
многое зависит от приложения и конкретных потребностей. Простейший пример чтобы начать работать:

```csharp
	var connectorType = Type.GetTypeFromProgID("V83.COMConnector");
	dynamic connector = Activator.CreateInstance(connectorType);
	var globalContext = connector.Connect("File=C:\my-1c-db;Usr=Администратор;Pwd=");
	var dataContext = DataContextFactory.CreateCOM(globalContext, typeof(Контрагенты).Assembly);
	var контрагент = new Контрагенты
	{
		ИНН = "1234567890",
		Наименование = "test-counterparty",
		ЮридическоеФизическоеЛицо = ЮридическоеФизическоеЛицо.ЮридическоеЛицо
	};
	dataContext.Save(контрагент);
	var контрагент2 = dataContext.Select<Контрагенты>().Single(x => x.Код == counterparty.Код);
	Assert.That(контрагент2.ИНН, Is.EqualTo("1234567890"));
```

* Документы и справочники Generator.exe превращает в классы, а перечисления - в enum-ы языка C#.
В примере выше для создания контрагента мы используем enum ЮридическоеФизическоеЛицо.

* Абстрагируем ссылки, про них в прикладном коде можно просто
забыть - создаем экемпляры классов объектной модели, присваиваем их свойствам других классов,
при маппинге на соответствующие COM-объекты ссылки между ними будут проставлены автоматически.

```csharp
	var контрагент = new Контрагенты
	{
		ИНН = "1234567890",
		Наименование = "test-counterparty",
		ЮридическоеФизическоеЛицо = ЮридическоеФизическоеЛицо.ЮридическоеЛицо
	};
	dataContext.Save(контрагент);
	var организация = dataContext.Select<Организация>().Single();
	var договор = new ДоговорыКонтрагентов
	{
		ВидДоговора = ВидыДоговоровКонтрагентов.СПокупателем,
		Наименование = "test name",
		Владелец = контрагент,
		Организация = организация
	};
	dataContext.Save(договор);
```

* Метод Save сохраняет все объекты, до которых может добраться по ссылкам. Пример выше можно
упростить следующим образом

```
	dataContext.Save(new ДоговорыКонтрагентов
	{
		ВидДоговора = ВидыДоговоровКонтрагентов.СПокупателем,
		Наименование = "test name",
		Владелец = new Контрагенты
		{
			ИНН = "1234567890",
			Наименование = "test-counterparty",
			ЮридическоеФизическоеЛицо = ЮридическоеФизическоеЛицо.ЮридическоеЛицо
		},
		Организация = dataContext.Select<Организация>().Single()
	});
```

* В запросах можно использовать стандартные Linq-операторы (Join, GroupBy пока не реализованы)

```csharp
public decimal GetCurrencyRateToDate(string currencyCode, DateTime date)
{
	return dataContext.Select<КурсыВалют>()
		.Where(x => x.Валюта.Код == currencyCode)
		.Where(x => x.Период <= date)
		.OrderByDescending(x => x.Период)
		.First()
		.Курс;
}
```

* Умеем обновлять отдельные строки табличных частей. Так, например, в таком примере

```csharp
var поступлениеТоваровУслуг = new ПоступлениеТоваровУслуг
{
	Дата = new DateTime(2016, 6, 1),
	Контрагент = new Контрагенты
	{
		ИНН = "7711223344",
		Наименование = "ООО Тестовый контрагент",
	},
	Услуги = new List<ПоступлениеТоваровУслуг.ТабличнаяЧастьУслуги>
	{
		new ПоступлениеТоваровУслуг.ТабличнаяЧастьУслуги
		{
			Номенклатура = new Номенклатура
			{
				Наименование = "стрижка"
			},
			Количество = 10,
			Содержание = "стрижка с кудряшками"
		},
		new ПоступлениеТоваровУслуг.ТабличнаяЧастьУслуги
		{
			Номенклатура = new Номенклатура
			{
				Наименование = "стрижка усов"
			},
			Количество = 10,
			Содержание = "стрижка бороды"
		}                
	};
dataContext.Save(поступлениеТоваровУслуг);
var t = поступлениеТоваровУслуг.Услуги[0];
поступлениеТоваровУслуг.Услуги[0] = поступлениеТоваровУслуг.Услуги[1];
поступлениеТоваровУслуг.Услуги[1] = t;
dataContext.Save(поступлениеТоваровУслуг);
```

на втором вызове Save будет сгенерирован единственный вызов метода `Сдвинуть`, меняющий
местами две строки.

* Через метод DataContextFactory.CreateInMemory() можно получить inmemory-реализацию
интерфейса IDataContext. Это здорово помогает при автоматизированном тестировании
безнес-логики. Весь граф сервисных объектов поверх IDataContext можно создать
целиком в inmemory-режиме, значительно ускорив этим прохождение тестов по сравнению
с работой с настоящей 1С.