/* La tabella e il riepilogo sono già disponibili prima del caricamento di questo file. */
jQuery(function ($) {
    "use strict";

    const statuses = new Map([
        ["Valid", ["Valido", "label-success"]],
        ["ExpiringSoon", ["In scadenza", "label-warning"]],
        ["Expired", ["Scaduto", "label-danger"]],
        ["Missing", ["Mancante", "label-default"]]
    ]);

    const language = {
        emptyTable: "Nessun attestato registrato",
        zeroRecords: "Nessun attestato corrisponde alla ricerca",
        info: "Righe da _START_ a _END_ di _TOTAL_",
        infoEmpty: "Nessuna riga",
        infoFiltered: "(su _MAX_ righe complessive)",
        lengthMenu: "Righe per pagina _MENU_",
        search: "Cerca nei corsi",
        loadingRecords: "Caricamento in corso…",
        processing: "Elaborazione in corso…",
        decimal: ",",
        thousands: ".",
        entries: { 1: "riga", _: "righe" },
        lengthLabels: { "-1": "Tutte" },
        paginate: { first: "Prima", last: "Ultima", previous: "Precedente", next: "Successiva" },
        aria: {
            orderable: ", attiva per ordinare",
            orderableRemove: ", attiva per rimuovere l’ordinamento",
            orderableReverse: ", attiva per invertire l’ordinamento",
            paginate: { first: "Prima pagina", last: "Ultima pagina", previous: "Pagina precedente",
                next: "Pagina successiva", number: "Pagina %d" }
        }
    };

    const table = new DataTable("#history", {
        language: language,
        autoWidth: false,
        // Gli attributi data-order (date ISO) sono rilevati automaticamente, anche sulle righe aggiunte via AJAX.
        order: []
    });
    $("#empty-history").remove();

    function dateCell(iso, emptyText) {
        const shown = iso ? iso.split("-").reverse().join("/") : emptyText;
        return $("<td>").attr("data-order", iso || "").text(shown);
    }

    function historyRow(item) {
        const [label, cssClass] = statuses.get(item.status) || ["Sconosciuto", "label-default"];
        const status = $("<td>").append($("<span>").addClass("label " + cssClass).text(label));
        if (!item.isCurrent) {
            status.append(document.createTextNode(" "), $("<span>").addClass("label label-default").text("Rinnovato"));
        }
        return $("<tr>").append(
            $("<td>").text(item.courseTitle + " (" + item.courseCode + ")"),
            $("<td>").text(item.category),
            $("<td>").text(item.isMandatory ? "Sì" : "No"),
            dateCell(item.completedOn, "—"),
            dateCell(item.expiresOn, "Non scade"),
            $("<td>").text(item.score === null ? "—" : String(item.score).replace(".", ",")),
            status
        )[0];
    }

    const button = $("#refresh").prop("hidden", false);
    button.on("click", function () {
        button.prop("disabled", true);
        $("#feedback").text("Aggiornamento in corso…");
        $("#refresh-error").empty();
        $.ajax({ url: "certificates-json.asp", method: "GET", dataType: "json", cache: false })
            .done(function (data) {
                const rows = data.items.map(historyRow);
                table.clear().rows.add(rows).draw(false);
                $("[data-count]").each(function () {
                    const status = this.dataset.count;
                    $(this).text(data.items.filter(item => item.isCurrent && item.status === status).length);
                });
                $("#feedback").text("Storico aggiornato.");
            })
            .fail(function (xhr) {
                $("#feedback").empty();
                $("#refresh-error").text(xhr.status === 401
                    ? "La sessione è scaduta. Ricarica la pagina per accedere di nuovo."
                    : "Si è verificato un errore. Riprova più tardi.");
            })
            .always(function () { button.prop("disabled", false); });
    });
});
