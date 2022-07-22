/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

'use strict';
$(function () {

    $(document).ready(function () {

        // initial GET of all registered queries upon page load
        $.ajax({
            type: 'GET',
            url: 'http://localhost:8099/api/Streams',
            success: function (ajaxdata) {
                $.each(ajaxdata.response, function (qname, qtext) {
                    var shortName = qtext.Name.replace(/ /g, '');
                    $('#queryTable tr:first').after('<tr><td id=' + shortName + '><input class="form-check-input" type="checkbox" value="">' + qtext.Name + '</td><td><span class="badge badge-info" id="' + shortName + '-span">Running</span></td></tr>');
                })
            }
        });

        $("#submitQueryBtn").click(function () {
            $('#spy-results').empty();
            var editor = ace.edit("query");
            var queryString = editor.getValue();
            var queryName = $('#queryName').val();
            var shortName = queryName.replace(/ /g, '');
            $.ajax({
                type: 'POST',
                url: 'http://localhost:8099/api/Streams?name=' + shortName + '&query=' + queryString + '&state=ACTIVE',
                data: queryString,
                success: function (ajaxdata) {
                    console.log(ajaxdata);
                    if (ajaxdata.response != 'OK') {
                        $('#spy-results').empty();
                        $('#spy-results').append('<p style=\'color: red\'>' + ajaxdata.response + '</p>');
                    }
                    else {
                        $('.badge-success').removeClass('badge-success').addClass('badge-info').text('Running');
                        if ($('#' + queryName).length === 0) {
                            $('#queryTable tr:first').after('<tr><td id=' + shortName + '><input class="form-check-input" type="checkbox" value="">' + shortName + '</td><td><span class="badge badge-success active-query"  id="' + shortName + '-span">Active</span></td></tr>');
                        }
                        else {
                            $('#' + shortName + "-span").removeClass('badge-info').removeClass('badge-secondary').addClass('badge-success').text('Active');
                        }
                    }
                },
                async: false
            });
        });

        $("#editQueryBtn").click(function () {
            // 1. check if only 1 is selected.  2. get the name value of the row that is selected  3. get the query 4. populate the edit window
            var editor = ace.edit("query");
            if ($("input:checked").length != 1) {
                alert('Select one entry to edit');
            }

            var editQueryName = "___NA"; 
            if ($("input:checked").length === 1) {
                editQueryName = $("input:checked").parent().text();
            }
            console.log('attemting a GET for ' + editQueryName);
            var statement = "NA";
            $.ajax({
                type: 'GET',
                url: 'http://localhost:8099/api/Streams/' + editQueryName,
                success: function (ajaxdata) {
                    console.log(ajaxdata);
                    var queryText = ajaxdata.response;
                    var formattedQuery = queryText.replace(' FROM ', ' \nFROM ').replace(' WHERE ', ' \nWHERE ').replace(/AND/gi, '\nAND');
                    $('#queryName').val(editQueryName);
                    editor.setValue(formattedQuery);
                },
                async: false
            });
        });

        $("#stopQueryBtn").click(function () {
            if ($("input:checked").length != 1) {
                alert('Select one entry to stop');
            }

            var stopQueryName = "___NA";
            if ($("input:checked").length === 1) {
                stopQueryName = $("input:checked").parent().text();
            }
            var shortStopName = stopQueryName.replace(/ /g, '');
            console.log('attemting a PUT for ' + stopQueryName);
            $.ajax({
                type: 'PUT',
                url: 'http://localhost:8099/api/Streams/' + stopQueryName,
                success: function (ajaxdata) {
                    console.log(ajaxdata);
                    if (ajaxdata.response != 'OK') {
                        $('#spy-results').empty();
                        $('#spy-results').append('<p style=\'color: red\'>' + ajaxdata.response + '</p>');
                    }
                    else {
                        $('#' + shortStopName + "-span").removeClass('badge-success').removeClass('badge-info').addClass('badge-secondary').text('Stopped');
                    }
                },
                async: false
            });
        });

        $("#startQueryBtn").click(function () {
            if ($("input:checked").length != 1) {
                alert('Select one entry to stop');
            }
            var startQueryName = $("input:checked").parent().text();
            var shortStartQueryName = startQueryName.replace(/ /g, '');
            console.log('attemting start for: ' + startQueryName);
            $.ajax({
                type: 'POST',
                url: 'http://localhost:8099/api/Streams?name=' + startQueryName + '&query=NA&state=START',
                success: function (ajaxdata) {
                    console.log(ajaxdata);
                    if (ajaxdata.response != 'OK') {
                        $('#spy-results').empty();
                        $('#spy-results').append('<p style=\'color: red\'>' + ajaxdata.response + '</p>');
                    }
                    else {
                        $('#' + shortStartQueryName + "-span").removeClass('badge-success').removeClass('badge-secondary').addClass('badge-info').text('Running');
                    }
                },
                async: false
            })
        });

        $("#deleteQueryBtn").click(function () {
            if ($("input:checked").length != 1) {
                alert('don\'t destroy all the things.  choose one.');
            }

            var destroyQueryName = "___NA"; 
            if ($("input:checked").length === 1) {
                destroyQueryName = $("input:checked").parent().text();
            }
            var shortDestoryName = destroyQueryName.replace(/ /g, '');
            console.log('attemting a PUT for ' + destroyQueryName);
            $.ajax({
                type: 'DELETE',
                url: 'http://localhost:8099/api/Streams/' + destroyQueryName,
                success: function (ajaxdata) {
                    console.log(ajaxdata);
                    if (ajaxdata.response != 'OK') {
                        $('#spy-results').empty();
                        $('#spy-results').append('<p style=\'color: red\'>' + ajaxdata.response + '</p>');
                    }
                    else {
                        location.reload();
                    }
                },
                async: false
            });
        });

    });
})